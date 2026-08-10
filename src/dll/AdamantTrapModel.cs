using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AdamantBlock
{
    // Gives the ModelEntity-shaped adamant blocks (the spikes trap) the mod's own texture,
    // while keeping the vanilla iron-spike silhouette.
    //
    // Why not TintColor, which blocks.xml carried until now:
    //   * The world path for a Shape="ModelEntity" block is NOT CloneModel (an earlier
    //     comment here said so and was wrong) but
    //     BlockShapeModelEntity.OnBlockEntityTransformBeforeActivated, which ends in
    //     BlockEntityData.SetMaterialColor("_Color", tintColor) - a MaterialPropertyBlock
    //     carrying a colour the shader MULTIPLIES onto the albedo.
    //   * This prefab's shader does not even declare _Color, so that write was a no-op.
    //     Its properties, logged in game: _Tint [Float], _Cutoff, _EmissionMultiply,
    //     _MainTex, _Normal, _Emissive, _RMOM, _MacroAO. A MaterialPropertyBlock silently
    //     ignores what the shader does not declare - no effect, no warning, nothing logged.
    //   * The general rule behind it: vanilla only ever sets TintColor on gun safes,
    //     munition boxes and chests, i.e. prefabs authored with a pale albedo so that the
    //     multiply produces the colour. No trap, no spike, nothing under Entities/Traps.
    //     ironSpikesTrapPrefab draws from one rust-brown metal material
    //     (Entities/Traps/Materials/ironSpikesTrap.mat, Entities/Traps/ironSpikesTrap.tga),
    //     so even a shader honouring _Color would have turned #7B4FB0 into dark mud.
    //
    // What does work is swapping the texture the material samples. The mod already ships
    // and loads adamant_diffuse/adamant_normal for the atlas injection, so this needs no
    // new asset and no Unity: clone the prefab's material, point the clone at those two,
    // and hand it to the instantiated renderers. The prefab and its material asset are
    // never written to, so the vanilla iron spikes trap keeps its own look.
    //
    // ⚠ OWNERSHIP. This is the part that took three releases to get right, so it is spelled
    // out. A runtime material must NEVER be shared between pooled models, because the
    // engine claims every runtime material it finds on a model it tears down:
    //
    //     GameObjectPool.DestroyObject(GameObject obj)      // 3.0.1, verified in IL
    //         obj.GetComponentsInChildren(tempRenderers);
    //         Utils.CleanupMaterialsOfRenderers(tempRenderers);   // -> Utils.CleanupMaterials
    //         UnityEngine.Object.Destroy(obj);
    //
    //     Utils.CleanupMaterials(mats)
    //         if ((bool)material && material.GetInstanceID() < 0)
    //             UnityEngine.Object.Destroy(material);
    //
    // A NEGATIVE instance id is Unity's marker for "created at runtime, not loaded from an
    // asset", which the pool reads as "an instance we made, so ours to free". Every
    // `new Material(...)` in here has one. So a single clone handed to every trap in the
    // world died as soon as ANY trap's model was destroyed.
    //
    // WHEN that hits decides what a repro has to look like: `PoolItem.updateTime` gates the
    // shrink and is pushed to `Time.time + 5f` only when an object is TAKEN from the pool -
    // never on return. So mining a trap more than 5 s after the last one was placed puts the
    // object into a pool whose clock has already expired, and the very next FrameUpdate frees
    // it: the trap next door goes magenta *immediately*. Do it within 5 s of placing and it
    // takes up to 5 s. Either way one object per update (maxDestroysPerUpdate = 1), which is
    // why it looked like it picked a random victim.
    // Reproduced deterministically (2026-08-10, 3.0.1): place two traps side by side, wait,
    // mine the first - the second goes flat magenta where it stands.
    //
    // Hence the split below:
    //   * one MASTER per source material, textures applied, kept in quarantine and never
    //     assigned to any renderer - what the pool cannot see, it cannot destroy;
    //   * one COPY per renderer, which the pool may destroy, and does, exactly when the
    //     model it belongs to dies. That is the lifecycle the engine expects.
    //
    // ⚠ The second trap: a material outlives what it is made of. Its SHADER belongs to the
    // Addressables bundle the prefab came from; releasing that bundle destroys the shader
    // and leaves the material pointing at nothing. Unity then draws the renderer with
    // Hidden/InternalErrorShader: flat bright magenta, silhouette intact, no exception,
    // nothing in the log. So nothing cached here is trusted across activations - see
    // StaleReason.
    internal static class AdamantTrapModel
    {
        // Confirmed in game (3.0.1): the trap material 'ironSpikesTrap' runs the shader
        // 'Game/Entity Tint Mask' and exposes exactly _MainTex, _Normal, _Emissive, _RMOM.
        // The first pass only probed _BumpMap/_NormalMap and therefore silently applied one
        // slot out of two - hence _Normal first here. The alternative spellings stay as a
        // cushion for other prefabs/game builds; every name goes through
        // Material.HasProperty, so a miss costs nothing.
        private static readonly string[] AlbedoProps = { "_MainTex", "_BaseMap" };
        private static readonly string[] NormalProps = { "_Normal", "_BumpMap", "_NormalMap" };
        private static readonly string[] SurfaceProps = { "_RMOM", "_MetallicGlossMap" };

        // What Unity substitutes when a material's shader is missing or failed to compile.
        // A material can also end up holding a destroyed Shader instead, which is the
        // fake-null case below - both draw the same magenta, so both count as stale.
        private const string ErrorShader = "Hidden/InternalErrorShader";

        // Without this the material keeps the iron spike's own surface map - a rusty, mostly
        // rough, barely metallic one - which is what "the gloss is missing" looks like.
        //
        // Same physical surface the block's atlas slice uses, only re-ordered: the atlas
        // channel is MOER (R metallic .7 / G AO .9 / B emission 0 / A roughness .35, see
        // AdamantAtlas.SurfaceResponse), while this slot spells its order in its own name -
        // R roughness, M metallic, O occlusion, M emissive. Alpha 0 is safe under either
        // reading of the last M (emissive or tint mask): no glow, no tint.
        // ⚠ That order is read off the property NAME, not proven. The shader's description for
        // the slot is the bare string "RMOM" (confirmed in a 3.0.1 log), the shader bundle is
        // LZ4-compressed, and no Managed DLL names the property - so the only confirmation is
        // how the trap looks in game (verified 3.0.1, 2026-07-31, and again at night on
        // 2026-08-10: no self-illumination, so nothing bright sits in an emission channel).
        // If it ever renders matte and non-metallic, swap R and G here first.
        private static readonly Color SurfaceRMOM = new Color(0.35f, 0.7f, 0.9f, 0f);
        private static Texture2D surfaceTex;

        // One entry per distinct source material. The Template is the quarantined master:
        // textures applied, never handed to a renderer, so no pool teardown can reach it.
        // The Source is kept alongside because that is what a master has to be rebuilt from
        // when its shader dies with the bundle - rebuilding from the dead master would only
        // copy the dead shader.
        private struct Master
        {
            public Material Source;
            public Material Template;
        }

        // Source material instance id -> its master. A cached entry whose Template is a
        // GENUINE null is the verdict "this material has none of the slots we fill", which
        // cannot change and is therefore not retried per placed block.
        private static readonly Dictionary<int, Master> masters = new Dictionary<int, Master>();

        // What a single material slot on a single renderer was given. Remembering the slot is
        // what makes this repairable at all: once a copy has been destroyed, the renderer
        // hands back a plain null with nothing on it to identify what used to be there, and
        // the old code could only report that and give up. With the slot remembered, no
        // identification is needed - the block is adamant, so the answer is known.
        private struct Slot
        {
            public int Key;         // masters key, or -1 for "not ours / unknown"
            public int MaterialId;  // instance id of the copy we put there
        }

        // Renderer instance id -> its slots. The Renderer reference is held on purpose: it
        // keeps the managed wrapper alive, which is what makes `Rend == null` a reliable
        // "this renderer is gone" for the pruning pass.
        private sealed class Skinned
        {
            public Renderer Rend;
            public Slot[] Slots;
        }

        private static readonly Dictionary<int, Skinned> skinned = new Dictionary<int, Skinned>();

        // Copies we made, by instance id. Needed for the one case the per-slot memory cannot
        // cover: after a prune, a renderer can come back carrying a copy we no longer have a
        // slot entry for, and without this it would look like a vanilla material and get
        // cloned again - a copy of a copy, inheriting whatever was wrong with it.
        private struct Copy
        {
            public Material Mat;
            public int Key;
        }

        private static readonly Dictionary<int, Copy> copyIndex = new Dictionary<int, Copy>();

        // Both tables are keyed by instance ids of objects the engine owns and destroys, so
        // they need a sweep. 2048 renderers is far past any real base and still cheap to walk.
        // The threshold moves with what survives a sweep, so a base that legitimately holds
        // more than that does not pay for a walk on every single block activation.
        private const int PruneFloor = 2048;
        private static int pruneAt = PruneFloor;

        private static bool described;
        private static bool stranded;         // reset by the next successful master build
        private static bool staleReported;    // the shader-died case, logged once, then counted
        private static bool sourcesReported;

        // Diagnostics. This class of fault cannot be reproduced on demand on the developer
        // machine, so whatever the next user log says has to be enough on its own.
        // `refills` is the counter that matters for the pool bug: it only moves when a copy
        // was destroyed under a renderer that is still standing.
        private static int builds;
        private static int copiesMade;
        private static int refills;
        private static int unknownSlots;
        private const int UnknownSlotLogLimit = 5;
        private static int copyLogAt = 1000;

        // Where the model currently being reskinned sits, for the log lines. Set by Apply,
        // read only on the diagnostic paths - main thread only, like everything here.
        private static string origin = "<unknown>";

        // Same discriminator the damage guard uses: matching on the material rather than on
        // a block name keeps this working for any further adamant model block.
        private static bool IsAdamant(Block block)
        {
            return block != null
                && block.blockMaterial != null
                && block.blockMaterial.id == AdamantGuard.MaterialId;
        }

        // Placed blocks. BlockEntityData.GetRenderers() is the same list the engine's own
        // tint/damage-state code walks.
        public static void Apply(Block block, BlockEntityData ebcd)
        {
            if (ebcd == null || !IsAdamant(block)) return;
            List<Renderer> rends = ebcd.GetRenderers();
            if (rends == null) return;
            origin = block.GetBlockName() + " at " + ebcd.pos;
            Prune();
            for (int i = 0; i < rends.Count; i++) Reskin(rends[i], false);
        }

        // Everything that instantiates the model outside the block-entity path (held item,
        // previews). Cheap to cover and keeps the two from disagreeing.
        //
        // Marked TRANSIENT, and that matters. These come from Object.Instantiate rather than
        // the pool, they are thrown away again in seconds, and there are a lot of them - a
        // 2.6 GUI run with the trap in hand produced 100 renderers in under two minutes. A
        // copy for one of those must therefore stay collectable: it is not entered into
        // copyIndex, so once its instance is gone nothing of ours references the material any
        // more and Resources.UnloadUnusedAssets can reclaim it. Retaining them was a leak of
        // one material per preview instantiation - invisible in a short session and ugly in a
        // long one, on exactly the memory-tight machines this mod keeps running into.
        public static void Apply(Block block, Transform root)
        {
            if (root == null || !IsAdamant(block)) return;
            Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
            origin = block.GetBlockName() + " (model instance, no block position)";
            Prune();
            for (int i = 0; i < rends.Length; i++) Reskin(rends[i], true);
        }

        private static void Reskin(Renderer rend, bool transient)
        {
            if (rend == null) return;

            // sharedMaterials, never materials: the getter for `materials` instantiates a
            // private copy per renderer behind our back, which would leave two competing
            // instances in the slot and hide which one is actually drawn.
            Material[] mats = rend.sharedMaterials;
            if (mats == null) return;

            int rid = rend.GetInstanceID();
            Skinned entry;
            if (!skinned.TryGetValue(rid, out entry) || entry.Slots.Length != mats.Length)
            {
                entry = new Skinned { Rend = rend, Slots = NewSlots(mats.Length) };
                skinned[rid] = entry;
            }
            entry.Rend = rend;

            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material current = mats[i];
                Slot slot = entry.Slots[i];

                // ReferenceEquals first, and the distinction is not cosmetic. A genuinely
                // empty slot has no managed object at all. A DESTROYED material may still be
                // a live managed object (Unity fake-null) that knows its instance id - but
                // only for as long as something holds a reference to that wrapper. Once the
                // wrapper is collected the same slot starts reading back as a plain null.
                // Both draw magenta, neither can be identified from the renderer, and that is
                // precisely why the slot memory above exists rather than another id lookup.
                bool alive = !ReferenceEquals(current, null) && current != null;
                bool isOurs = alive && slot.Key >= 0 && current.GetInstanceID() == slot.MaterialId;

                if (isOurs)
                {
                    string stale = StaleReason(current);
                    if (stale == null) continue;   // the normal case, and the common one
                    ReportStale(stale);
                }

                int key = slot.Key;
                if (!isOurs)
                {
                    if (alive)
                    {
                        int id = current.GetInstanceID();
                        Copy known;
                        if (copyIndex.TryGetValue(id, out known))
                        {
                            // One of ours, just not in the slot table any more (pruned).
                            key = known.Key;
                            if (StaleReason(current) == null)
                            {
                                entry.Slots[i] = new Slot { Key = key, MaterialId = id };
                                continue;
                            }
                        }
                        else
                        {
                            // A vanilla material: either the first sighting of this model, or
                            // the pool handing a recycled renderer back with the prefab's own
                            // material on it.
                            key = masters.ContainsKey(id) || BuildMaster(id, current) != null
                                  ? id : -1;
                        }
                    }
                    else if (slot.Key < 0)
                    {
                        // Nothing on the renderer and nothing remembered. With a single model
                        // in play there is still exactly one right answer, so use it instead
                        // of leaving a magenta trap standing.
                        key = OnlyMasterKey();
                        if (key < 0)
                        {
                            ReportUnknownSlot();
                            continue;
                        }
                    }
                }

                if (key < 0) continue;

                Material copy = MakeCopy(key, transient);
                if (copy == null) continue;

                mats[i] = copy;
                entry.Slots[i] = new Slot { Key = key, MaterialId = copy.GetInstanceID() };
                changed = true;
                if (!alive) ReportRefill();
            }

            if (changed) rend.sharedMaterials = mats;
        }

        private static Slot[] NewSlots(int count)
        {
            var slots = new Slot[count];
            for (int i = 0; i < count; i++) slots[i] = new Slot { Key = -1, MaterialId = 0 };
            return slots;
        }

        // The renderer's own material. Negative instance id, so the pool will destroy it
        // together with the model it belongs to - which is the entire point: it takes only
        // its own model down with it, never the trap next door.
        private static Material MakeCopy(int key, bool transient)
        {
            Material template = Template(key);
            if (template == null) return null;

            Material copy;
            try
            {
                copy = new Material(template);
                copy.name = template.name + " (renderer)";
            }
            catch (Exception e)
            {
                Log.Error("[AdamantBlock] could not instance the trap material: " + e);
                return null;
            }

            copiesMade++;

            // Only the ones on pooled block models are retained - see Apply(Block, Transform)
            // for why a preview copy must stay collectable. The cost of not retaining it is
            // that it cannot be recognised as ours later, which is fine: its renderer dies
            // with its instance and is never seen again.
            if (!transient) copyIndex[copy.GetInstanceID()] = new Copy { Mat = copy, Key = key };

            // The POSITIVE proof line, and it exists because its absence cost a verification
            // round: the master-build line is written before the first copy, so a clean log
            // showed "0 renderer copies" and could not tell a working session apart from one
            // where no copy was ever made.
            //
            // First copy, then decades. A fixed interval would say "still running" roughly
            // every 90 s while a trap is in hand (measured: 1.13 copies/s, most of them
            // preview instances) and carry no information at all: the proof is the first
            // line, and the only thing worth hearing about afterwards is `refills` moving,
            // which reports itself.
            if (copiesMade == 1 || copiesMade == copyLogAt)
            {
                if (copiesMade == copyLogAt)
                    copyLogAt = copyLogAt <= 100000000 ? copyLogAt * 10 : int.MaxValue;

                Log.Out("[AdamantBlock] trap renderer materials: " + copiesMade + " copies, "
                        + refills + " refills, " + builds + " master builds, "
                        + skinned.Count + " renderers tracked, " + copyIndex.Count
                        + " retained | " + origin);
            }

            return copy;
        }

        // The master for `key` as it must be right now, rebuilt if what we last made has
        // gone stale. Null = nothing can be supplied (no bundle, no matching slots, or the
        // source material is gone as well).
        private static Material Template(int key)
        {
            Master master;
            if (!masters.TryGetValue(key, out master)) return null;

            // ReferenceEquals, not ==: a genuine null is the cached verdict "this material
            // has none of the slots we fill", which stays true. A Unity fake-null is a
            // master that was destroyed, which does not - and has to be rebuilt rather than
            // handed out.
            if (ReferenceEquals(master.Template, null)) return null;

            string stale = StaleReason(master.Template);
            if (stale == null) return master.Template;

            string headline = "[AdamantBlock] the trap master material has gone stale ("
                            + stale + ") - anything still drawing with a copy of it shows as"
                            + " flat magenta with an intact silhouette. Seen on " + origin;

            if (master.Source != null)
            {
                Log.Warning(headline + " - rebuilding it from the vanilla material");
                return BuildMaster(key, master.Source);
            }

            // Both halves went with the same bundle, which is the normal case rather than
            // the exception: the shader died *because* that bundle was released, and the
            // vanilla material was in it.
            Material substitute = OnlyLiveTemplate();
            if (substitute != null)
            {
                Log.Warning(headline + " - and the vanilla material with it, so the working"
                            + " master from a later model load takes its place; that is what"
                            + " this renderer would have been given had it been instantiated"
                            + " then");
                masters[key] = new Master { Source = null, Template = substitute };
                return substitute;
            }

            ReportStranded(stale);
            return null;
        }

        private static Material BuildMaster(int key, Material src)
        {
            // No bundle (dedicated server, missing or malformed adamant.unity3d). The trap
            // then simply keeps the vanilla iron look - the same graceful degradation the
            // atlas injector falls back to. Deliberately NOT cached as a verdict:
            // EnsureSources is already sticky for the deterministic case, and caching here
            // would swallow the recoverable one - textures that were unloaded and can be
            // loaded again - for the rest of the process.
            Texture2D diffuse = AdamantAtlas.SourceDiffuse;
            if (diffuse == null)
            {
                ReportNoSources();
                return null;
            }

            // Cloning a material whose shader already died just makes a second magenta one.
            // Not cached either - the next prefab load brings a live material.
            if (src.shader == null)
            {
                Log.Warning("[AdamantBlock] the vanilla material '" + SafeName(src)
                            + "' has no live shader, so cloning it would only produce"
                            + " another magenta model - left vanilla for now (" + origin + ")");
                return null;
            }

            Material template = null;
            try
            {
                Describe(src);

                template = new Material(src);
                template.name = src.name + "_adamant";

                // Quarantine. HideAndDontSave carries DontUnloadUnusedAsset, so the master
                // also survives an asset sweep; what keeps it safe from the POOL, though, is
                // simply that it is never assigned to a renderer - Utils.CleanupMaterials
                // only ever sees what a renderer carries.
                template.hideFlags = HideFlags.HideAndDontSave;

                // Which slots were hit is worth naming: the first version silently applied
                // one of two (it probed _BumpMap while this shader calls it _Normal) and the
                // count alone would not have shown that.
                var applied = new List<string>();
                SetFirst(template, AlbedoProps, diffuse, applied);
                SetFirst(template, NormalProps, AdamantAtlas.SourceNormal, applied);
                SetFirst(template, SurfaceProps, SurfaceTexture(), applied);

                if (applied.Count == 0)
                {
                    Log.Warning("[AdamantBlock] trap material '" + SafeName(src) + "' (shader '"
                                + (src.shader != null ? src.shader.name : "<none>")
                                + "') has none of the expected texture slots - model left vanilla");
                    UnityEngine.Object.Destroy(template);
                    template = null;
                }
                else
                {
                    builds++;
                    stranded = false;
                    staleReported = false;

                    // Everything a later report needs without a repro. The shader name is
                    // the one that decides the magenta question; the albedo line is what
                    // rules the texture path in or out, including the mipmap limit that
                    // broke the block in 1.2.2.
                    Log.Out("[AdamantBlock] trap master material built from '" + SafeName(src)
                            + "': " + string.Join(", ", applied.ToArray())
                            + " | shader '" + ShaderName(template) + "'"
                            + " | albedo " + DescribeTexture(diffuse)
                            + " | normal " + DescribeTexture(AdamantAtlas.SourceNormal)
                            + " | build #" + builds + ", " + copiesMade + " renderer copies, "
                            + refills + " refills so far | " + origin);
                }
            }
            catch (Exception e)
            {
                Log.Error("[AdamantBlock] could not retexture the trap model: " + e);
                template = null;
            }

            masters[key] = new Master { Source = src, Template = template };
            return template;
        }

        // Used when a slot is empty and unremembered. Only answers when there is exactly one
        // candidate: with more than one distinct source material in play, picking between
        // them would be a guess, and a wrong material is worse than a magenta one that
        // repairs itself on the next pool reload.
        private static int OnlyMasterKey()
        {
            int found = -1;
            foreach (KeyValuePair<int, Master> pair in masters)
            {
                if (ReferenceEquals(pair.Value.Template, null)) continue;
                if (StaleReason(pair.Value.Template) != null) continue;
                if (found >= 0) return -1;
                found = pair.Key;
            }
            return found;
        }

        private static Material OnlyLiveTemplate()
        {
            int key = OnlyMasterKey();
            Master master;
            return key >= 0 && masters.TryGetValue(key, out master) ? master.Template : null;
        }

        // Why this material must not be used, or null when it is fine. Returning the
        // reason rather than a bool is the point: "shader gone" and "textures gone" have
        // different causes and different fixes, they are indistinguishable from the
        // outside (both draw magenta), and neither shows up anywhere else in the log.
        private static string StaleReason(Material mat)
        {
            if (mat == null) return "material destroyed";

            // Order matters. A destroyed Shader is Unity-fake-null, and reading .name off
            // it throws MissingReferenceException instead of returning anything.
            Shader shader = mat.shader;
            if (shader == null) return "shader destroyed with its bundle";
            if (shader.name == ErrorShader) return "shader replaced by " + ErrorShader;

            for (int i = 0; i < AlbedoProps.Length; i++)
                if (mat.HasProperty(AlbedoProps[i]))
                    return mat.GetTexture(AlbedoProps[i]) != null ? null : "albedo texture unloaded";
            return null;
        }

        // Drops what the engine has destroyed under us. Holding the Renderer and Material
        // references is what makes this possible at all - a bare instance id cannot be asked
        // whether its object still exists.
        private static void Prune()
        {
            if (skinned.Count <= pruneAt && copyIndex.Count <= pruneAt * 2) return;

            var deadRenderers = new List<int>();
            foreach (KeyValuePair<int, Skinned> pair in skinned)
                if (pair.Value.Rend == null) deadRenderers.Add(pair.Key);
            for (int i = 0; i < deadRenderers.Count; i++) skinned.Remove(deadRenderers[i]);

            var deadCopies = new List<int>();
            foreach (KeyValuePair<int, Copy> pair in copyIndex)
                if (pair.Value.Mat == null) deadCopies.Add(pair.Key);
            for (int i = 0; i < deadCopies.Count; i++) copyIndex.Remove(deadCopies[i]);

            int live = skinned.Count > copyIndex.Count ? skinned.Count : copyIndex.Count;
            pruneAt = live * 2 > PruneFloor ? live * 2 : PruneFloor;

            Log.Out("[AdamantBlock] trap tables pruned: " + deadRenderers.Count
                    + " destroyed renderers and " + deadCopies.Count + " destroyed materials"
                    + " dropped, " + skinned.Count + " renderers / " + copyIndex.Count
                    + " materials left, next sweep at " + pruneAt);
        }

        // The pool bug, after the fix: a copy was destroyed under a renderer that is still
        // standing. It should not happen any more - the pool only frees the materials of the
        // model it is tearing down - so if this counter moves, something else is claiming
        // them and the next report needs to say so.
        private static void ReportRefill()
        {
            refills++;
            if (refills > 1) return;
            Log.Warning("[AdamantBlock] a trap renderer's own material had been destroyed"
                        + " while the renderer was still in use (that is the flat-magenta"
                        + " case) - refilled from the master. Seen on " + origin
                        + " [further ones counted, not logged]");
        }

        // Logged once per generation - on the machine where this happens it would otherwise
        // repeat for every renderer of every trap in view. Reset by the next master build
        // that succeeds, so a later occurrence is reported again.
        private static void ReportStale(string reason)
        {
            if (staleReported) return;
            staleReported = true;
            Log.Warning("[AdamantBlock] a trap renderer was carrying a material that had gone"
                        + " stale (" + reason + ") - replaced. Seen on " + origin
                        + " [further ones counted, not logged]");
        }

        private static void ReportStranded(string reason)
        {
            if (stranded) return;
            stranded = true;
            Log.Warning("[AdamantBlock] the trap material is unusable (" + reason + ") and"
                        + " neither the vanilla material nor any working master is left to put"
                        + " in its place, so the model stays as it is until the pool reloads"
                        + " it. Seen on " + origin);
        }

        // A slot that is empty, unremembered, and cannot be answered because more than one
        // source material is in play. Rare by construction now, but it has to stay visible:
        // it would otherwise look exactly like a healthy skip.
        private static void ReportUnknownSlot()
        {
            unknownSlots++;
            if (unknownSlots > UnknownSlotLogLimit) return;
            Log.Warning("[AdamantBlock] an adamant model renderer has an empty material slot"
                        + " and more than one candidate material, so it cannot be filled from"
                        + " here (draws magenta) - " + origin
                        + (unknownSlots == UnknownSlotLogLimit ? " [further ones not logged]" : ""));
        }

        private static void ReportNoSources()
        {
            if (sourcesReported) return;
            sourcesReported = true;
            Log.Warning("[AdamantBlock] no source textures for the trap model"
                        + " - the model keeps the vanilla iron look");
        }

        // A uniform surface needs no file and no compression: 2x2 is the smallest texture
        // that samples cleanly at any mip. Linear, because these are data channels, not
        // colour. Kept alive for the process - it is shared by every master.
        private static Texture2D SurfaceTexture()
        {
            if (surfaceTex != null) return surfaceTex;

            surfaceTex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            surfaceTex.name = "adamant_rmom";
            surfaceTex.wrapMode = TextureWrapMode.Repeat;
            surfaceTex.SetPixels(new[] { SurfaceRMOM, SurfaceRMOM, SurfaceRMOM, SurfaceRMOM });
            surfaceTex.Apply(false, false);
            surfaceTex.hideFlags = HideFlags.HideAndDontSave;
            return surfaceTex;
        }

        private static void SetFirst(Material mat, string[] names, Texture tex, List<string> applied)
        {
            if (tex == null) return;
            for (int i = 0; i < names.Length; i++)
            {
                if (!mat.HasProperty(names[i])) continue;
                mat.SetTexture(names[i], tex);
                applied.Add(names[i]);
                return;
            }
        }

        private static string ShaderName(Material mat)
        {
            Shader shader = mat.shader;
            return shader == null ? "<destroyed>" : shader.name;
        }

        private static string SafeName(Material mat)
        {
            return mat == null || string.IsNullOrEmpty(mat.name) ? "<unnamed material>" : mat.name;
        }

        // The full state of what was actually applied, not the name alone. `activeMipmapLimit`
        // is in here because it is the one property that already cost this mod a release:
        // below Full texture quality it is non-zero unless the importer exempts the texture,
        // and everything downstream of it fails silently.
        private static string DescribeTexture(Texture2D tex)
        {
            if (tex == null) return "<none>";
            return (string.IsNullOrEmpty(tex.name) ? "<unnamed>" : tex.name)
                   + " " + tex.width + "x" + tex.height + "/" + tex.mipmapCount + "mip/"
                   + tex.graphicsFormat + "/limit " + tex.activeMipmapLimit
                   + (tex.isReadable ? "/readable" : "");
        }

        // Logged once. A shader whose slots are not what we assumed is the one failure mode
        // that would otherwise be invisible (wrong-looking model, no error anywhere), so the
        // actual slot list goes into the log rather than being taken on faith. The DESCRIPTION
        // is logged alongside the name because packed maps sometimes spell their channel order
        // there ("RMOM (R:Roughness G:Metallic B:AO A:Emissive)") - that would be the only
        // offline-proof source for how SurfaceRMOM above has to be ordered, since the shader
        // bundle is compressed and the game's Managed DLLs never name the property. On 3.0.1
        // the description is the bare string "RMOM", so it settles nothing; the in-game night
        // check does.
        private static void Describe(Material src)
        {
            if (described) return;
            described = true;

            Shader shader = src.shader;
            var props = new List<string>();
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    var type = shader.GetPropertyType(i);
                    if (type != UnityEngine.Rendering.ShaderPropertyType.Texture
                        && type != UnityEngine.Rendering.ShaderPropertyType.Float
                        && type != UnityEngine.Rendering.ShaderPropertyType.Range)
                        continue;
                    props.Add(shader.GetPropertyName(i) + " [" + type + "] \""
                              + shader.GetPropertyDescription(i) + "\"");
                }
            }

            Log.Out("[AdamantBlock] trap model material '" + SafeName(src) + "', shader '"
                    + (shader != null ? shader.name : "<none>") + "', properties: "
                    + (props.Count > 0 ? string.Join(" | ", props.ToArray()) : "<none>"));
        }
    }

    // The engine's own hook for "this block entity's transform exists and is about to be
    // switched on" - the same method that applies TintColor and the damage state, so the
    // renderers are guaranteed to be there.
    [HarmonyPatch(typeof(BlockShapeModelEntity),
                  nameof(BlockShapeModelEntity.OnBlockEntityTransformBeforeActivated))]
    internal static class Patch_BlockShapeModelEntity_OnBlockEntityTransformBeforeActivated
    {
        private static void Postfix(BlockShapeModelEntity __instance, BlockEntityData _ebcd)
        {
            AdamantTrapModel.Apply(__instance.block, _ebcd);
        }
    }

    // The other place the model gets instantiated from (Object.Instantiate on the prefab,
    // followed by the TintColor application) - covers the non-chunk uses of the same model.
    [HarmonyPatch(typeof(BlockShapeModelEntity), nameof(BlockShapeModelEntity.CloneModel))]
    internal static class Patch_BlockShapeModelEntity_CloneModel
    {
        private static void Postfix(BlockShapeModelEntity __instance, Transform __result)
        {
            AdamantTrapModel.Apply(__instance.block, __result);
        }
    }
}

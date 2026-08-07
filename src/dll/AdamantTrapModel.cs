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
    // new asset and no Unity: clone the prefab's material once, point the clone at those
    // two, and hand it to the instantiated renderers. The prefab and its material asset
    // are never written to, so the vanilla iron spikes trap keeps its own look.
    //
    // ⚠ The clone outlives what it is made of. It is a runtime Material held by a static
    // cache, while its SHADER belongs to the Addressables bundle the prefab came from;
    // releasing that bundle destroys the shader and leaves the clone pointing at nothing.
    // Unity then draws the renderer with Hidden/InternalErrorShader: flat bright magenta,
    // silhouette intact, no exception, nothing in the log. That is why nothing cached here
    // is ever trusted across activations - see StaleReason, which is the whole point of
    // this file's caching layer and the reason it looks more defensive than it needs to.
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

        // Without this the clone keeps the iron spike's own surface map - a rusty, mostly
        // rough, barely metallic one - which is what "the gloss is missing" looks like.
        //
        // Same physical surface the block's atlas slice uses, only re-ordered: the atlas
        // channel is MOER (R metallic .7 / G AO .9 / B emission 0 / A roughness .35, see
        // AdamantAtlas.SurfaceResponse), while this slot spells its order in its own name -
        // R roughness, M metallic, O occlusion, M emissive. Alpha 0 is safe under either
        // reading of the last M (emissive or tint mask): no glow, no tint.
        // ⚠ That order is read off the property NAME, not proven. The shader's description for
        // the slot is the bare string "RMOM", the shader bundle is LZ4-compressed, and no
        // Managed DLL names the property - so the only confirmation is how the trap looks in
        // game (verified 3.0.1, 2026-07-31). If it ever renders matte and non-metallic, swap
        // R and G here first.
        private static readonly Color SurfaceRMOM = new Color(0.35f, 0.7f, 0.9f, 0f);
        private static Texture2D surfaceTex;

        // One entry per distinct source material - not per renderer, not per placed block.
        // The SOURCE is kept alongside the clone because a renderer that has already been
        // reskinned no longer carries the material it was made from, and that is exactly
        // the moment the clone has to be rebuilt from it. Cloning the stale clone instead
        // would copy the very shader that died.
        private struct Skin
        {
            public Material Source;
            public Material Clone;
        }

        // Source material instance id -> the skin made for it. A cached entry whose Clone
        // is a GENUINE null is the verdict "this material has none of the slots we fill",
        // which cannot change and is therefore not retried per placed block. Everything
        // that CAN change is revalidated instead.
        private static readonly Dictionary<int, Skin> skins = new Dictionary<int, Skin>();

        // Clone instance id -> the skins key it was made for. The engine pools the model
        // GameObjects and hands the same renderer back on the next activation, by which
        // point its material is already ours - this is how that is recognised.
        //
        // Retired clones stay in here on purpose. A renderer can still be carrying one
        // long after it was replaced, and dropping the entry would send that renderer
        // down the "unknown vanilla material" path, where it would be cloned again - a
        // clone of a clone, inheriting the dead shader that caused the replacement.
        private static readonly Dictionary<int, int> ours = new Dictionary<int, int>();

        private static bool described;
        private static bool sourcesReported;
        private static bool stranded;         // reset by the next successful Build
        private static bool recoverReported;  // the magenta case, logged once, then counted

        // Diagnostics. The whole point of the counters is that this bug cannot be
        // reproduced on the developer machine: whatever the next user log says has to be
        // enough on its own. `builds` counts materials made, `repairs` renderers that were
        // found carrying a stale one, `emptySlots` renderers that came in with no material
        // at all (which also draws magenta, but is not something this class can repair).
        private static int builds;
        private static int repairs;
        private static int emptySlots;
        private const int EmptySlotLogLimit = 5;

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
            for (int i = 0; i < rends.Count; i++) Reskin(rends[i]);
        }

        // Everything that instantiates the model outside the block-entity path (held item,
        // previews). Cheap to cover and keeps the two from disagreeing.
        public static void Apply(Block block, Transform root)
        {
            if (root == null || !IsAdamant(block)) return;
            Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
            origin = block.GetBlockName() + " (model instance, no block position)";
            for (int i = 0; i < rends.Length; i++) Reskin(rends[i]);
        }

        private static void Reskin(Renderer rend)
        {
            if (rend == null) return;

            // sharedMaterials, never materials: reading .materials would instantiate a
            // private copy per renderer (and the pool holds many), which is exactly the
            // allocation this cache exists to avoid.
            Material[] mats = rend.sharedMaterials;
            if (mats == null) return;

            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material replacement = Resolve(mats[i]);
                if (replacement == null) continue;
                mats[i] = replacement;
                changed = true;
            }
            if (changed) rend.sharedMaterials = mats;
        }

        // The material this slot should be carrying, or null for "leave it alone".
        private static Material Resolve(Material current)
        {
            // ReferenceEquals, not ==, and this distinction is the whole fix. A genuinely
            // empty slot has nothing left to identify it by. A DESTROYED material is still
            // a live managed object that knows its instance id - and that is the case that
            // draws magenta on a pooled renderer, because the engine hands the same
            // renderer back with our dead clone still on it. `== null` answers true for
            // both; treating them alike is what left the trap magenta until a restart.
            if (ReferenceEquals(current, null))
            {
                ReportEmptySlot();
                return null;
            }
            if (current == null) return Recover(current);

            int id = current.GetInstanceID();

            int key;
            if (ours.TryGetValue(id, out key))
            {
                // Ours - and revalidated rather than trusted, on every single activation.
                // This is the check that the first version got wrong: it only asked
                // whether the TEXTURES were still there. They come from this mod's own
                // bundle and are almost never the thing that goes; the shader comes from
                // the game's and is.
                if (StaleReason(current) == null) return null;

                Material fresh = Current(key);
                if (fresh == null || fresh.GetInstanceID() == id) return null;
                repairs++;
                return fresh;
            }

            // Not ours. Either a vanilla material we have never seen, or one we already
            // have a verdict on.
            return skins.ContainsKey(id) ? Current(id) : Build(id, current);
        }

        // A renderer that came back from the pool carrying a material we made and Unity has
        // since destroyed - confirmed in a 3.0.0 GUI run right after a world reload, logged
        // as "material destroyed". The renderer draws magenta and will never be handed a
        // vanilla material again, so this is the only chance to put a working one back.
        private static Material Recover(Material dead)
        {
            Material replacement = null;
            try
            {
                int key;
                if (ours.TryGetValue(dead.GetInstanceID(), out key)) replacement = Current(key);
            }
            catch (Exception e)
            {
                // Reading anything off a destroyed UnityEngine.Object can throw. The id is
                // only a shortcut to the right entry; without it the fallback still applies.
                Log.Warning("[AdamantBlock] could not identify a destroyed trap material: "
                            + e.Message);
            }

            if (replacement == null) replacement = OnlyLiveClone();
            if (replacement == null)
            {
                ReportStranded("a destroyed material is on the renderer");
                return null;
            }

            repairs++;
            if (!recoverReported)
            {
                recoverReported = true;
                Log.Warning("[AdamantBlock] a pooled renderer came back carrying a trap"
                            + " material Unity had destroyed - that draws flat magenta with an"
                            + " intact silhouette. Replaced with a working one. Seen on "
                            + origin + " [further ones counted, not logged]");
            }
            return replacement;
        }

        // The material key `key` should be drawn with right now, rebuilt if what we last
        // made has gone stale. Null = nothing can be supplied (no bundle, no matching
        // slots, or the source material is gone as well).
        private static Material Current(int key)
        {
            Skin skin;
            if (!skins.TryGetValue(key, out skin)) return null;

            // ReferenceEquals, not ==: a genuine null is the cached verdict "this material
            // has none of the slots we fill", which stays true. A Unity fake-null is a
            // clone that was destroyed, which does not - and has to be rebuilt rather than
            // handed out.
            if (ReferenceEquals(skin.Clone, null)) return null;

            string stale = StaleReason(skin.Clone);
            if (stale == null) return skin.Clone;

            string headline = "[AdamantBlock] the trap material we applied has gone stale ("
                            + stale + ") - that is what a flat magenta trap with an intact"
                            + " silhouette looks like. Seen on " + origin;

            if (skin.Source != null)
            {
                Log.Warning(headline + " - rebuilding it from the vanilla material");
                return Build(key, skin.Source);
            }

            // Both halves went with the same bundle, which is the normal case rather than
            // the exception: the shader died *because* that bundle was released, and the
            // vanilla material was in it.
            Material substitute = OnlyLiveClone();
            if (substitute != null)
            {
                Log.Warning(headline + " - and the vanilla material with it, so the working"
                            + " clone from a later model load takes its place; that is what"
                            + " this renderer would have been given had it been instantiated"
                            + " then");
                // Recorded, so the next renderer carrying the same dead clone is answered
                // straight from the cache instead of walking (and re-logging) all of this.
                skins[key] = new Skin { Source = null, Clone = substitute };
                return substitute;
            }

            ReportStranded(stale);
            return null;
        }

        // Logged once per generation - on the machine where this happens it would otherwise
        // repeat for every renderer of every trap in view. Reset by the next Build that
        // succeeds, so a later occurrence is reported again.
        private static void ReportStranded(string reason)
        {
            if (stranded) return;
            stranded = true;
            Log.Warning("[AdamantBlock] the trap material is unusable (" + reason + ") and"
                        + " neither the vanilla material nor any working clone is left to put"
                        + " in its place, so the model stays as it is until the pool reloads"
                        + " it. Seen on " + origin);
        }

        // Last resort for the case above. By the time a renderer surfaces with a dead clone,
        // a later prefab load has usually already produced a working one for the same model.
        // Only used when there is exactly one candidate: with more than one distinct source
        // material in play, picking between them would be a guess, and a wrong material is
        // worse than a magenta one that repairs itself on the next pool reload.
        private static Material OnlyLiveClone()
        {
            Material found = null;
            foreach (Skin skin in skins.Values)
            {
                if (ReferenceEquals(skin.Clone, null) || StaleReason(skin.Clone) != null) continue;
                if (found != null) return null;
                found = skin.Clone;
            }
            return found;
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

        private static Material Build(int key, Material src)
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

            // Cloning a material whose shader already died just makes a second magenta
            // one. Not cached either - the next prefab load brings a live material.
            if (src.shader == null)
            {
                Log.Warning("[AdamantBlock] the vanilla material '" + SafeName(src)
                            + "' has no live shader, so cloning it would only produce"
                            + " another magenta model - left vanilla for now (" + origin + ")");
                return null;
            }

            Material clone = null;
            try
            {
                Describe(src);

                clone = new Material(src);
                clone.name = src.name + "_adamant";

                // Which slots were hit is worth naming: the first version silently applied
                // one of two (it probed _BumpMap while this shader calls it _Normal) and the
                // count alone would not have shown that.
                var applied = new List<string>();
                SetFirst(clone, AlbedoProps, diffuse, applied);
                SetFirst(clone, NormalProps, AdamantAtlas.SourceNormal, applied);
                SetFirst(clone, SurfaceProps, SurfaceTexture(), applied);

                if (applied.Count == 0)
                {
                    Log.Warning("[AdamantBlock] trap material '" + SafeName(src) + "' (shader '"
                                + (src.shader != null ? src.shader.name : "<none>")
                                + "') has none of the expected texture slots - model left vanilla");
                    UnityEngine.Object.Destroy(clone);
                    clone = null;
                }
                else
                {
                    builds++;
                    stranded = false;
                    ours[clone.GetInstanceID()] = key;

                    // Everything a later report needs without a repro. The shader name is
                    // the one that decides the magenta question; the albedo line is what
                    // rules the texture path in or out, including the mipmap limit that
                    // broke the block in 1.2.2.
                    Log.Out("[AdamantBlock] trap model retextured on '" + SafeName(src) + "': "
                            + string.Join(", ", applied.ToArray())
                            + " | shader '" + ShaderName(clone) + "'"
                            + " | albedo " + DescribeTexture(diffuse)
                            + " | normal " + DescribeTexture(AdamantAtlas.SourceNormal)
                            + " | build #" + builds + ", " + repairs + " renderer repairs so far"
                            + " | " + origin);
                }
            }
            catch (Exception e)
            {
                Log.Error("[AdamantBlock] could not retexture the trap model: " + e);
                clone = null;
            }

            skins[key] = new Skin { Source = src, Clone = clone };
            return clone;
        }

        // A renderer that comes in with an empty material slot draws magenta too, but
        // there is nothing left on it to identify what it used to be, so it cannot be
        // repaired from here. It still has to be visible in the log, because it would
        // otherwise look exactly like the case above and send the next diagnosis down the
        // wrong path. Capped, since one bad pool could otherwise fill the log.
        private static void ReportEmptySlot()
        {
            emptySlots++;
            if (emptySlots > EmptySlotLogLimit) return;
            Log.Warning("[AdamantBlock] an adamant model renderer has an empty material slot"
                        + " (draws magenta, cannot be repaired from here) - " + origin
                        + (emptySlots == EmptySlotLogLimit ? " [further ones not logged]" : ""));
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
        // colour. Kept alive for the process - it is shared by every clone.
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
        // is logged alongside the name because packed maps spell their channel order there
        // ("RMOM (R:Roughness G:Metallic B:AO A:Emissive)") - that is the only offline-proof
        // source for how SurfaceRMOM above has to be ordered, since the shader bundle is
        // compressed and the game's Managed DLLs never name the property.
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

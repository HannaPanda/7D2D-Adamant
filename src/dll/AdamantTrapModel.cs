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

        // Source material instance id -> our reskinned clone. A cached null means "this
        // material has none of the slots we fill", which cannot change and so is not retried
        // per placed block. Anything that *can* change - textures unloaded, clone destroyed -
        // is not cached as a verdict; see GetClone. One clone per distinct source material,
        // not per renderer or per block instance.
        private static readonly Dictionary<int, Material> clones = new Dictionary<int, Material>();

        // Instance ids of the clones themselves. The engine pools the model GameObjects and
        // hands the same renderer back on the next activation, by which point its material
        // is already ours - without this, that clone would be cloned again on every reuse.
        private static readonly HashSet<int> ours = new HashSet<int>();

        private static bool described;

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
            for (int i = 0; i < rends.Count; i++) Reskin(rends[i]);
        }

        // Everything that instantiates the model outside the block-entity path (held item,
        // previews). Cheap to cover and keeps the two from disagreeing.
        public static void Apply(Block block, Transform root)
        {
            if (root == null || !IsAdamant(block)) return;
            Renderer[] rends = root.GetComponentsInChildren<Renderer>(true);
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
                Material src = mats[i];
                if (src == null) continue;
                if (ours.Contains(src.GetInstanceID()))
                {
                    // Already ours - unless an asset unload took its textures out from
                    // under it, in which case it now draws as the vanilla trap and has to
                    // be replaced like any other source material. Without this the pooled
                    // renderers would never reach the check in GetClone at all.
                    if (HasLiveTextures(src)) continue;
                    ours.Remove(src.GetInstanceID());
                }

                Material clone = GetClone(src);
                if (clone == null) continue;
                mats[i] = clone;
                changed = true;
            }
            if (changed) rend.sharedMaterials = mats;
        }

        private static Material GetClone(Material src)
        {
            int key = src.GetInstanceID();
            Material clone;
            if (clones.TryGetValue(key, out clone))
            {
                // ReferenceEquals, not ==: a genuine null is the cached verdict "this
                // material has none of the slots we fill", which stays true. A Unity
                // fake-null is a clone that was destroyed by an asset unload, which does
                // not - and has to be rebuilt rather than handed out.
                if (ReferenceEquals(clone, null)) return null;
                if (clone != null && HasLiveTextures(clone)) return clone;
                if (clone != null) ours.Remove(clone.GetInstanceID());
                clones.Remove(key);
            }

            Texture2D diffuse = AdamantAtlas.SourceDiffuse;
            if (diffuse == null)
            {
                // No bundle (dedicated server, missing or malformed adamant.unity3d). The
                // trap then simply keeps the vanilla iron look - same graceful degradation
                // the atlas injector falls back to. Deliberately NOT cached as a null
                // verdict: EnsureSources is already sticky for the deterministic case, and
                // caching here would swallow the recoverable one - textures that were
                // unloaded and can be loaded again - for the rest of the process.
                return null;
            }

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
                    Log.Warning("[AdamantBlock] trap material '" + src.name + "' (shader '"
                                + (src.shader != null ? src.shader.name : "<none>")
                                + "') has none of the expected texture slots - model left vanilla");
                    UnityEngine.Object.Destroy(clone);
                    clone = null;
                }
                else
                {
                    ours.Add(clone.GetInstanceID());
                    Log.Out("[AdamantBlock] trap model retextured on '" + src.name + "': "
                            + string.Join(", ", applied.ToArray()));
                }
            }
            catch (Exception e)
            {
                Log.Error("[AdamantBlock] could not retexture the trap model: " + e);
                clone = null;
            }

            clones[key] = clone;
            return clone;
        }

        // A clone outlives the textures it samples when an asset unload destroys them, and
        // an untextured material on this shader draws as exactly the plain iron trap the
        // retexture exists to replace. The stale clone is dropped from the caches but not
        // destroyed: pooled renderers may still hold it, and destroying it would leave them
        // with no material at all, which is worse than a briefly wrong one.
        private static bool HasLiveTextures(Material clone)
        {
            for (int i = 0; i < AlbedoProps.Length; i++)
                if (clone.HasProperty(AlbedoProps[i]))
                    return clone.GetTexture(AlbedoProps[i]) != null;
            return true;
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

            Log.Out("[AdamantBlock] trap model material '" + src.name + "', shader '"
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

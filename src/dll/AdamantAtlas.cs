using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace AdamantBlock
{
    // Puts the mod's own 512² texture into the game's opaque block texture atlas,
    // so the shapes="All" set renders with it and NO core mod is required.
    //
    // How the game builds that atlas (verified against 3.0.1 Assembly-CSharp):
    //   MeshDescription.LoadTextureArraysForQuality
    //     -> loadSingleArray x3   (loads ta_opaque[_n|_s]_<quality>.asset from
    //                              Addressables into TexDiffuse/TexNormal/TexSpecular)
    //     -> TextureAtlas.LoadTextureAtlas  (copies those three refs into the atlas)
    // TextureAtlasBlocks.LoadTextureAtlas overrides that and first calls
    // LoadTextureAtlasFromMetadata, which (re)builds TextureAtlas.uvMapping - the
    // array a block's "Texture" property indexes into (BlockShapeNew.renderFace).
    //
    // So a Postfix on TextureAtlasBlocks.LoadTextureAtlas sees both finished: the
    // three Texture2DArrays and a fresh uvMapping. We grow each array by one slice,
    // fill it, append one uvMapping entry, and hand the blocks that entry's index.
    //
    // Deliberately NOT done: registering a paint (BlockTextureData / painting.xml).
    // Paint IDs are persisted per painted face in the save, and dynamically assigned
    // ones drift when the set of installed paint mods changes ("Missing paint ID XML
    // entry: N for block ..."). A block's own texture id comes from blocks.xml and is
    // re-resolved every start, so staying out of the paint id space keeps saves safe.
    // The cost is that adamant is not offered in the paint tool. That is the trade.
    internal static class AdamantAtlas
    {
        // What blocks.xml ships as the block's Texture value. Doubles as the donor
        // entry: its uvMapping record supplies material/color for our own entry, and
        // its atlas slice pre-fills ours, which guarantees a format-correct specular
        // (MOER) slice without shipping a third texture.
        public const int DonorTextureId = 356; // vanilla steel

        private const string DiffuseAsset = "adamant_diffuse";
        private const string NormalAsset = "adamant_normal";
        private const string BundlePath = "/Resources/adamant.unity3d?";

        // The third atlas channel packs metallic / AO / emission / roughness into one
        // texture. We ship no such file: adamant wants a completely uniform surface
        // response, so it is generated at load. These are the exact values the mod used
        // while it still went through a paint entry ("512:512:0.7:0.9:0:0.35"), i.e. a
        // proven-good look. Inheriting the donor's channel instead would paint steel's
        // gloss and scratch pattern over our albedo - visible as "steel showing through".
        private static readonly Color SurfaceResponse = new Color(0.7f, 0.9f, 0f, 0.35f);

        private static string bundleUri;          // "#<mod path>/Resources/adamant.unity3d?"
        private static Texture2D srcDiffuse, srcNormal;
        private static bool sourcesTried;

        // The arrays we allocated. Kept so a second LoadTextureAtlas pass can tell
        // "already ours" from "vanilla reloaded them", and so the Unload guard below
        // can recognize them.
        private static Texture2DArray ownDiffuse, ownNormal, ownSpecular;

        // uvMapping index our texture currently sits at, and the id the blocks were
        // last bound to. -1 = nothing injected (dedicated server, missing bundle, ...),
        // in which case the blocks simply keep the vanilla fallback from blocks.xml.
        internal static int AtlasTextureId = -1;
        private static int boundTextureId = -1;

        // Whoever grows the atlas last is the only one who cannot invalidate someone
        // else's offsets, so the first injection is deferred to Block.LateInitAll - after
        // painting.xml, and therefore after paint frameworks like OcbCustomTextures have
        // registered theirs. Growing the array before them shifts the slices their paint
        // entries point at, which shows up as vanilla paints wearing our texture.
        // Later rebuilds (a texture-quality change) come back through the atlas postfix,
        // where those frameworks have already re-applied inside loadSingleArray - so there
        // the postfix itself is the late one.
        private static bool injected;
        private static TextureAtlas lastAtlas;
        private static MeshDescriptionCollection lastCollection;

        public static void Configure(Mod mod)
        {
            if (mod != null && !string.IsNullOrEmpty(mod.Path))
                bundleUri = "#" + mod.Path.Replace('\\', '/') + BundlePath;
        }

        // The two textures the mod ships, loaded on demand. Exposed because the trap model
        // (AdamantTrapModel) needs the same two files and must not depend on the atlas path
        // having run - the two are independent: the atlas can bail out on a dedicated server
        // or a texture-quality rebuild can happen long after a trap was placed.
        internal static Texture2D SourceDiffuse { get { EnsureSources(); return srcDiffuse; } }
        internal static Texture2D SourceNormal { get { EnsureSources(); return srcNormal; } }

        // Sticky: a missing or malformed bundle is a deterministic failure, and this is
        // reached once per placed trap block, so it must not retry or re-log.
        private static bool EnsureSources()
        {
            if (!sourcesTried)
            {
                sourcesTried = true;
                if (bundleUri != null)
                {
                    srcDiffuse = DataLoader.LoadAsset<Texture2D>(bundleUri + DiffuseAsset, false);
                    srcNormal = DataLoader.LoadAsset<Texture2D>(bundleUri + NormalAsset, false);
                    if (srcDiffuse == null || srcNormal == null)
                        Log.Warning("[AdamantBlock] " + BundlePath.Trim('/', '?')
                                    + " has no usable " + DiffuseAsset + "/" + NormalAsset
                                    + " - block keeps texture " + DonorTextureId
                                    + " and the trap keeps the vanilla model");
                }
            }
            return srcDiffuse != null && srcNormal != null;
        }

        public static bool Owns(Texture tex)
        {
            return tex != null && (ReferenceEquals(tex, ownDiffuse)
                                || ReferenceEquals(tex, ownNormal)
                                || ReferenceEquals(tex, ownSpecular));
        }

        // Postfix body. Runs on every atlas (re)build, including a texture-quality
        // change mid-game, and must therefore be idempotent.
        // Called on every atlas build. The first one only takes note; a rebuild re-applies
        // right away, because the arrays it just loaded are vanilla again and our slice
        // would otherwise be gone until the next restart.
        public static void Remember(TextureAtlas atlas, MeshDescriptionCollection collection)
        {
            lastAtlas = atlas;
            lastCollection = collection;
            if (injected) Extend();
        }

        // Runs the deferred first injection once every config is parsed.
        public static void ExtendWhenReady()
        {
            if (!injected) Extend();
            BindBlocks();
        }

        // Everything is resolved live, never from what the atlas postfix saw: rendering
        // goes through MeshDescription.meshes[MeshIndex].textureAtlas.uvMapping
        // (BlockShapeNew.renderFace), and that atlas is not necessarily the instance the
        // load ran on. Appending to the wrong one leaves the block with a texture id past
        // the end of the live array, which renderFace silently swaps for a default - a
        // wood-looking block with no error anywhere.
        public static void Extend()
        {
            if (bundleUri == null) return;

            MeshDescription[] all = MeshDescription.meshes;
            if (all == null || all.Length <= MeshDescription.cIndexOpaque)
                all = lastCollection != null ? lastCollection.Meshes : null;
            if (all == null || all.Length <= MeshDescription.cIndexOpaque) return;
            MeshDescription opaque = all[MeshDescription.cIndexOpaque];
            if (opaque == null) return;

            TextureAtlas atlas = opaque.textureAtlas ?? lastAtlas;
            if (atlas == null) return;

            UVRectTiling[] map = atlas.uvMapping;
            if (map == null || map.Length <= DonorTextureId) return;

            try
            {
                // uvMapping is rebuilt from the atlas metadata on every call, so our
                // entry has to be re-appended every time - that is what actually
                // publishes the texture id, and it must happen even when the arrays
                // are already ours.
                bool ready = ownDiffuse != null && ReferenceEquals(opaque.TexDiffuse, ownDiffuse);
                Texture[] retired = ready ? null : GrowArrays(opaque, map[DonorTextureId].index);
                if (!ready && retired == null) return;

                // Order matters: ReloadTextureArrays binds the materials from the ATLAS
                // fields (Material.mainTexture <- textureAtlas.diffuseTexture, _BumpMap <-
                // normalTexture, _MetallicGlossMap <- specularTexture), not from the
                // MeshDescription. Rebinding before these three assignments leaves every
                // chunk material on the old array, where our slice index is out of range -
                // and an out-of-range slice clamps to the last one instead of failing.
                atlas.diffuseTexture = opaque.TexDiffuse;
                atlas.normalTexture = opaque.TexNormal;
                atlas.specularTexture = opaque.TexSpecular;
                opaque.ReloadTextureArrays(false);
                VerifyBinding(opaque);

                AtlasTextureId = AppendMapping(atlas, ownDiffuse.depth - 1);
                injected = true;
                BindBlocks();

                // Only once every reference has been moved over: the old arrays are
                // Addressables assets, and at ~one atlas worth of VRAM each they are far
                // too big to keep around. Anything still pointing at them here would see
                // a destroyed texture, so this must stay the last step.
                Release(opaque, retired);
            }
            catch (Exception e)
            {
                Log.Error("[AdamantBlock] atlas injection failed, block keeps texture "
                          + DonorTextureId + ": " + e);
            }
        }

        private static void Release(MeshDescription opaque, Texture[] retired)
        {
            if (retired == null) return;
            try
            {
                for (int i = 0; i < retired.Length; i++)
                    opaque.Unload(ref retired[i]);
            }
            catch (Exception e)
            {
                // A leaked atlas costs memory; a half-published one costs the texture.
                // Never let this failure undo the work above.
                Log.Warning("[AdamantBlock] could not release the replaced atlas arrays: " + e.Message);
            }
        }

        // Replaces the three opaque arrays with one-slice-longer copies of themselves and
        // hands back the arrays that were swapped out, for the caller to release once
        // everything points at the new ones. Returns null when the source textures are
        // unusable - nothing is changed then and the block stays on its fallback texture.
        private static Texture[] GrowArrays(MeshDescription opaque, int donorSlice)
        {
            var diffuse = opaque.TexDiffuse as Texture2DArray;
            var normal = opaque.TexNormal as Texture2DArray;
            var specular = opaque.TexSpecular as Texture2DArray;
            if (diffuse == null || normal == null || specular == null)
                return null; // headless / textures not loaded - nothing to do

            // Read off the source while it is alive: it gets released later, and touching
            // a destroyed UnityEngine.Object throws with an empty message.
            int atlasSize = diffuse.width;
            int oldDepth = diffuse.depth;

            if (!EnsureSources())
                return null;

            Log.Out("[AdamantBlock] atlas: live=" + (ReferenceEquals(opaque.textureAtlas, lastAtlas) ? "same as load" : "DIFFERENT from load")
                    + ", uvMapping " + (opaque.textureAtlas != null && opaque.textureAtlas.uvMapping != null
                                        ? opaque.textureAtlas.uvMapping.Length.ToString() : "?")
                    + " entries vs " + (lastAtlas != null && lastAtlas.uvMapping != null
                                        ? lastAtlas.uvMapping.Length.ToString() : "?") + " at load time");
            Log.Out("[AdamantBlock] atlas channels: diffuse " + Describe(diffuse) + " <- " + Describe(srcDiffuse)
                    + " | normal " + Describe(normal) + " <- " + Describe(srcNormal)
                    + " | specular " + Describe(specular));

            Texture2D uniformSpecular = BuildUniform(specular, SurfaceResponse);
            Texture2DArray grownDiffuse = CopyWithExtraSlice(diffuse, donorSlice, srcDiffuse);
            Texture2DArray grownNormal = CopyWithExtraSlice(normal, donorSlice, srcNormal);
            Texture2DArray grownSpecular = CopyWithExtraSlice(specular, donorSlice, uniformSpecular);
            if (uniformSpecular != null) UnityEngine.Object.Destroy(uniformSpecular);
            if (grownDiffuse == null || grownNormal == null || grownSpecular == null)
            {
                Destroy(grownDiffuse); Destroy(grownNormal); Destroy(grownSpecular);
                return null;
            }

            // A quality change hands us freshly loaded vanilla arrays; drop the copies
            // made for the previous quality level instead of leaking them.
            Destroy(ownDiffuse); Destroy(ownNormal); Destroy(ownSpecular);

            Texture prevDiffuse = opaque.TexDiffuse, prevNormal = opaque.TexNormal, prevSpecular = opaque.TexSpecular;
            opaque.TexDiffuse = ownDiffuse = grownDiffuse;
            opaque.TexNormal = ownNormal = grownNormal;
            opaque.TexSpecular = ownSpecular = grownSpecular;

            Log.Out("[AdamantBlock] opaque atlas " + atlasSize + "px: slice "
                    + (grownDiffuse.depth - 1) + " added (" + oldDepth + " -> "
                    + grownDiffuse.depth + ")");
            return new[] { prevDiffuse, prevNormal, prevSpecular };
        }

        // A single-colour texture in the atlas' own compressed format, built by filling an
        // uncompressed one and letting Unity compress it - the only runtime path that
        // produces block-compressed data without an offline tool. Returns null when the
        // result does not match the atlas exactly; the caller then falls back to the donor
        // slice, which looks wrong but never breaks.
        private static Texture2D BuildUniform(Texture2DArray like, Color value)
        {
            Texture2D flat = null;
            try
            {
                // The three atlas channels do not agree on colour space (3.0.1: diffuse
                // DXT1_SRGB, normal DXT5_UNorm, specular DXT5_SRGB), and Compress() keeps
                // whatever the staging texture was created as. Take it from the target
                // instead of assuming - a mismatch here is rejected further down and costs
                // the whole channel.
                bool linear = !GraphicsFormatUtility.IsSRGBFormat(like.graphicsFormat);
                flat = new Texture2D(like.width, like.height, TextureFormat.RGBA32, true, linear);
                var pixels = new Color[like.width * like.height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = value;
                flat.SetPixels(pixels);
                flat.Apply(true, false);
                flat.Compress(true);
                flat.Apply(true, false);

                if (flat.graphicsFormat == like.graphicsFormat) return flat;

                Log.Warning("[AdamantBlock] generated surface response is " + flat.graphicsFormat
                            + ", atlas wants " + like.graphicsFormat
                            + " - falling back to the donor slice (steel gloss will show)");
            }
            catch (Exception e)
            {
                Log.Warning("[AdamantBlock] could not generate the surface response: " + e.Message);
            }
            if (flat != null) UnityEngine.Object.Destroy(flat);
            return null;
        }

        // The block silently renders something else when the chunk material still samples
        // the old array, so this is worth stating in the log rather than assuming.
        private static void VerifyBinding(MeshDescription opaque)
        {
            Material chunk = opaque.materials != null && opaque.materials.Length > 0 ? opaque.materials[0] : null;
            if (chunk == null)
            {
                Log.Warning("[AdamantBlock] opaque mesh has no material to rebind"
                            + " (bTextureArray=" + opaque.bTextureArray + ")");
                return;
            }

            bool bound = ReferenceEquals(chunk.mainTexture, ownDiffuse);
            if (bound) Log.Out("[AdamantBlock] chunk material bound to the extended atlas");
            else Log.Warning("[AdamantBlock] chunk material still samples the old atlas"
                             + " (bTextureArray=" + opaque.bTextureArray
                             + ") - the block will show a neighbouring texture");
        }

        private static string Describe(Texture tex)
        {
            if (tex == null) return "<none>";
            var array = tex as Texture2DArray;
            return tex.width + "px/" + tex.mipmapCount + "mip/" + tex.graphicsFormat
                   + (array != null ? "/" + array.depth + "slices" : "");
        }

        // depth+1 copy of an atlas array. The extra slice starts as a duplicate of the
        // donor slice (so it is always valid and correctly formatted) and is then
        // overwritten with `overlay` when one is given.
        private static Texture2DArray CopyWithExtraSlice(Texture2DArray source, int donorSlice, Texture2D overlay)
        {
            int slices = source.depth;
            int mips = source.mipmapCount;
            var copy = new Texture2DArray(source.width, source.height, slices + 1, source.graphicsFormat,
                                          mips > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None,
                                          mips);
            copy.filterMode = source.filterMode;
            copy.wrapMode = source.wrapMode;
            copy.anisoLevel = source.anisoLevel;

            for (int slice = 0; slice < slices; slice++)
                for (int mip = 0; mip < mips; mip++)
                    Graphics.CopyTexture(source, slice, mip, copy, slice, mip);

            if (donorSlice >= 0 && donorSlice < slices)
                for (int mip = 0; mip < mips; mip++)
                    Graphics.CopyTexture(source, donorSlice, mip, copy, slices, mip);

            if (overlay != null)
                Overlay(copy, slices, overlay);
            return copy;
        }

        // Writes a single texture into one slice. The atlas is loaded at half size on
        // TexQuality 1, so the matching mip of the source has to be picked instead of
        // its mip 0 - copying a 512 mip into a 256 slice is what produces Unity's
        // "invalid source mip level" spam and a garbage slice.
        private static void Overlay(Texture2DArray target, int slice, Texture2D source)
        {
            if (source.graphicsFormat != target.graphicsFormat)
            {
                Log.Warning("[AdamantBlock] " + source.name + " is " + source.graphicsFormat
                            + ", atlas wants " + target.graphicsFormat + " - slice left as steel");
                return;
            }

            int skip = 0;
            int width = source.width;
            while (width > target.width && skip < source.mipmapCount - 1) { width >>= 1; skip++; }
            if (width != target.width)
            {
                Log.Warning("[AdamantBlock] " + source.name + " is " + source.width
                            + "px with " + source.mipmapCount + " mips and cannot fill a "
                            + target.width + "px atlas slice - slice left as steel");
                return;
            }

            int mips = Math.Min(target.mipmapCount, source.mipmapCount - skip);
            for (int mip = 0; mip < mips; mip++)
                Graphics.CopyTexture(source, 0, mip + skip, target, slice, mip);
        }

        // One more uvMapping record, cloned from the donor so material and color match
        // a metal block, pointing at our new slice as a full 1x1 tile.
        private static int AppendMapping(TextureAtlas atlas, int slice)
        {
            UVRectTiling[] map = atlas.uvMapping;
            int id = map.Length;
            Array.Resize(ref map, id + 1);

            UVRectTiling entry = map[DonorTextureId];
            entry.index = slice;
            entry.uv = new Rect(0f, 0f, 1f, 1f);
            entry.blockW = 1;
            entry.blockH = 1;
            entry.bGlobalUV = false;
            entry.textureName = "adamant";
            entry.color = Color.white;

            map[id] = entry;
            atlas.uvMapping = map;
            return id;
        }

        // Rewrites the texture id on every block made of adamant. Matching on the
        // material rather than on block names covers the whole shapes="All" set, which
        // the engine expands into one Block per shape.
        public static void BindBlocks()
        {
            if (AtlasTextureId < 0 || Block.list == null) return;

            int from = boundTextureId >= 0 ? boundTextureId : DonorTextureId;
            if (from == AtlasTextureId) return;

            int touched = 0;
            for (int i = 0; i < Block.list.Length; i++)
            {
                Block block = Block.list[i];
                if (block == null || block.blockMaterial == null) continue;
                if (block.blockMaterial.id != AdamantGuard.MaterialId) continue;
                if (block.textureInfos == null) continue;

                for (int channel = 0; channel < block.textureInfos.Length; channel++)
                {
                    if (block.textureInfos[channel].singleTextureId == from)
                    {
                        block.textureInfos[channel].singleTextureId = AtlasTextureId;
                        touched++;
                    }
                    int[] sides = block.textureInfos[channel].sideTextureIds;
                    if (sides == null) continue;
                    for (int side = 0; side < sides.Length; side++)
                        if (sides[side] == from) { sides[side] = AtlasTextureId; touched++; }
                }
            }

            if (touched > 0)
            {
                boundTextureId = AtlasTextureId;
                Log.Out("[AdamantBlock] texture id " + AtlasTextureId + " applied to "
                        + touched + " adamant block faces");
            }
        }

        private static void Destroy(Texture2DArray array)
        {
            if (array != null) UnityEngine.Object.Destroy(array);
        }
    }

    // The one place where the finished atlas and a fresh uvMapping exist together. On the
    // very first build this only records them - the injection itself waits for
    // Block.LateInitAll so that paint frameworks get to compute their offsets against an
    // untouched atlas. A later rebuild (texture-quality change) does inject from here,
    // because by then those frameworks have already re-applied inside loadSingleArray.
    // TextureAtlasTerrain routes its own load through this method too, hence the index check.
    [HarmonyPatch(typeof(TextureAtlasBlocks), nameof(TextureAtlasBlocks.LoadTextureAtlas))]
    internal static class Patch_TextureAtlasBlocks_LoadTextureAtlas
    {
        private static void Postfix(TextureAtlasBlocks __instance, int _idx,
                                    MeshDescriptionCollection _tac, bool _bLoadTextures)
        {
            if (!_bLoadTextures || _idx != MeshDescription.cIndexOpaque) return;
            AdamantAtlas.Remember(__instance, _tac);
        }
    }

    // Config load order is painting.xml -> blocks.xml, so this is the first moment at which
    // every paint mod has had its turn AND the blocks that need the id exist.
    [HarmonyPatch(typeof(Block), nameof(Block.LateInitAll))]
    internal static class Patch_Block_LateInitAll
    {
        private static void Postfix()
        {
            AdamantAtlas.ExtendWhenReady();
        }
    }

    // MeshDescription.Unload releases an Addressables handle and calls
    // Resources.UnloadAsset, neither of which applies to an array we allocated
    // ourselves - that combination logs errors and leaks the texture. Destroy ours
    // properly instead and skip the original.
    [HarmonyPatch(typeof(MeshDescription), nameof(MeshDescription.Unload))]
    internal static class Patch_MeshDescription_Unload
    {
        private static bool Prefix(ref Texture tex)
        {
            if (!AdamantAtlas.Owns(tex)) return true;
            UnityEngine.Object.Destroy(tex);
            tex = null;
            return false;
        }
    }
}

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

        // Name on our uvMapping entry. Also how a later pass recognizes an entry it already
        // appended, so it must stay stable.
        private const string TextureName = "adamant";

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
        private static bool sourcesTried;    // one load attempt has been made
        private static bool sourcesMissing;  // ... and it failed for good

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
        private static bool started;
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

        // Two different failures live here and they need opposite handling.
        //
        // "The bundle is not there" (dedicated server, missing or malformed file) is
        // deterministic: this is reached once per placed trap block, so it must not retry
        // and must not re-log - hence sourcesMissing.
        //
        // "The textures were there and are gone again" is not. Unloading an asset bundle
        // destroys its objects whether or not anything still references them, and a
        // destroyed UnityEngine.Object compares equal to null while the field still holds
        // it (Unity's fake-null). Treating that as the deterministic case is what left the
        // trap wearing the plain vanilla iron look for the rest of the process after a
        // world reload. Reload instead.
        private static bool EnsureSources()
        {
            if (srcDiffuse != null && srcNormal != null) return true;
            if (sourcesMissing || bundleUri == null) return false;

            bool reloading = sourcesTried;
            sourcesTried = true;

            srcDiffuse = DataLoader.LoadAsset<Texture2D>(bundleUri + DiffuseAsset, false);
            srcNormal = DataLoader.LoadAsset<Texture2D>(bundleUri + NormalAsset, false);

            // Texture quality does not only shrink the atlas, it also puts every texture the
            // game loaded at a non-zero mipmap limit - ours included. Graphics.CopyTexture
            // refuses to copy across differing limits, and a Texture2DArray has no limit at
            // all, so without this the atlas fill silently does nothing below Full and the
            // block renders as steel. The importer flag (ignoreMipmapLimit: 1 in the .meta)
            // says the same thing; this repeats it so a bundle rebuilt without it cannot
            // reintroduce the bug.
            ExemptFromMipmapLimit(srcDiffuse);
            ExemptFromMipmapLimit(srcNormal);

            if (srcDiffuse == null || srcNormal == null)
            {
                sourcesMissing = true;
                Log.Warning("[AdamantBlock] " + BundlePath.Trim('/', '?')
                            + " has no usable " + DiffuseAsset + "/" + NormalAsset
                            + " - block keeps texture " + DonorTextureId
                            + " and the trap keeps the vanilla model");
                return false;
            }

            if (reloading)
                Log.Out("[AdamantBlock] source textures had been unloaded - reloaded from the bundle");
            return true;
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
            // Retried on every rebuild after the deferred start, whether or not the last
            // attempt worked: a quality change hands us vanilla arrays again, so failing
            // at one quality level must not disable the mod for the next one.
            if (started) Extend();
        }

        // Runs the deferred first injection once every config is parsed.
        public static void ExtendWhenReady()
        {
            started = true;
            Extend();
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
                if (!ready && retired == null) { RevertBlocks(); return; }

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
                RevertBlocks();
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

            // Albedo and normal are the feature; the surface response is a gloss detail that
            // has always been allowed to fall back to the donor's channel. Treating all three
            // as required is what turned a cosmetic miss into a block with no texture at all.
            Texture2D uniformSpecular = BuildUniform(specular, SurfaceResponse);
            Texture2DArray grownDiffuse = CopyWithExtraSlice(diffuse, donorSlice, srcDiffuse, true);
            Texture2DArray grownNormal = CopyWithExtraSlice(normal, donorSlice, srcNormal, true);
            Texture2DArray grownSpecular = CopyWithExtraSlice(specular, donorSlice, uniformSpecular, false);
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
                flat.name = "adamant_surface_response";
                var pixels = new Color[like.width * like.height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = value;
                flat.SetPixels(pixels);
                flat.Apply(true, false);
                flat.Compress(true);
                flat.Apply(true, false);

                // Set here and not right after the constructor: Compress and Apply rebuild
                // the texture and the flag does not survive them. Setting it too early is
                // how an unnamed texture still at limit 1 reached Overlay.
                ExemptFromMipmapLimit(flat);

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
        private static Texture2DArray CopyWithExtraSlice(Texture2DArray source, int donorSlice,
                                                        Texture2D overlay, bool overlayRequired)
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

            if (overlay != null && !Overlay(copy, slices, overlay, overlayRequired) && overlayRequired)
            {
                Destroy(copy);
                return null;
            }
            return copy;
        }

        // Writes a single texture into one slice. The atlas is loaded at half size on
        // TexQuality 1, so the matching mip of the source has to be picked instead of
        // its mip 0 - copying a 512 mip into a 256 slice is what produces Unity's
        // "invalid source mip level" spam and a garbage slice.
        //
        // Every failure here is the whole feature failing: the slice was pre-filled from
        // the donor, so the block would render as flawless vanilla steel with nothing but
        // an INF line to show for it. That is why these are errors and why the caller
        // throws the copy away instead of publishing it.
        private static bool Overlay(Texture2DArray target, int slice, Texture2D source, bool required)
        {
            if (source.graphicsFormat != target.graphicsFormat)
                return Fail(required, Name(source) + " is " + source.graphicsFormat
                                      + ", atlas wants " + target.graphicsFormat);

            // Texture quality does not only shrink the atlas, it also puts the textures the
            // game has loaded at a non-zero mipmap limit - ours included, since they come in
            // through the same asset pipeline. Graphics.CopyTexture refuses to copy across
            // limits ("different mipmap limits. Source 1, Destination 0"), so the two source
            // textures must be exempt from it: "Ignore Mipmap Limit" in the importer, which
            // is ignoreMipmapLimit: 1 in their .meta.
            if (source.activeMipmapLimit != 0)
                return Fail(required, Name(source) + " is at mipmap limit "
                                      + source.activeMipmapLimit
                                      + " while a Texture2DArray is always at 0, so"
                                      + " Graphics.CopyTexture rejects the pair");

            int skip = 0;
            int width = source.width;
            while (width > target.width && skip < source.mipmapCount - 1) { width >>= 1; skip++; }
            if (width != target.width)
                return Fail(required, Name(source) + " is " + source.width + "px with "
                                      + source.mipmapCount + " mips and cannot fill a "
                                      + target.width + "px atlas slice");

            int mips = Math.Min(target.mipmapCount, source.mipmapCount - skip);
            for (int mip = 0; mip < mips; mip++)
                Graphics.CopyTexture(source, 0, mip + skip, target, slice, mip);
            return true;
        }

        // Same diagnosis, two severities. On albedo and normal a miss costs the whole
        // feature, so it is an error and the caller discards the copy. On the surface
        // response the slice simply keeps the donor's channel, which is the fallback this
        // path has always had - loud enough to find, not fatal.
        private static bool Fail(bool required, string reason)
        {
            if (required) Log.Error("[AdamantBlock] " + reason + " - injection aborted");
            else Log.Warning("[AdamantBlock] " + reason
                             + " - slice keeps the donor channel (steel gloss will show)");
            return false;
        }

        // A texture built at runtime has no name until one is set, and "[AdamantBlock]  is
        // at mipmap limit 1" is not a message anyone can act on.
        private static string Name(Texture tex)
        {
            return tex == null || string.IsNullOrEmpty(tex.name) ? "<unnamed texture>" : tex.name;
        }

        // One more uvMapping record, cloned from the donor so material and color match
        // a metal block, pointing at our new slice as a full 1x1 tile.
        private static int AppendMapping(TextureAtlas atlas, int slice)
        {
            UVRectTiling[] map = atlas.uvMapping;

            // A world reload comes back through here with the atlas itself untouched, so
            // without this every reload would append another copy of the same entry and
            // grow uvMapping for the life of the process.
            if (map.Length > 0 && map[map.Length - 1].textureName == TextureName
                                && map[map.Length - 1].index == slice)
                return map.Length - 1;

            int id = map.Length;
            Array.Resize(ref map, id + 1);

            UVRectTiling entry = map[DonorTextureId];
            entry.index = slice;
            entry.uv = new Rect(0f, 0f, 1f, 1f);
            entry.blockW = 1;
            entry.blockH = 1;
            entry.bGlobalUV = false;
            entry.textureName = TextureName;
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
            if (AtlasTextureId >= 0) Rebind(AtlasTextureId);
        }

        // An injection that bailed leaves the blocks on an id from the previous atlas, and
        // the uvMapping the game just rebuilt is too short to contain it - renderFace then
        // silently substitutes a default, which looks even less like adamant than the steel
        // fallback. Put them back on the id blocks.xml ships.
        private static void RevertBlocks()
        {
            AtlasTextureId = -1;
            Rebind(DonorTextureId);
        }

        private static void Rebind(int to)
        {
            if (Block.list == null) return;

            // Two ids can legitimately be sitting on an adamant face: the one we last bound,
            // and the fallback from blocks.xml. Loading a world re-parses blocks.xml and
            // rebuilds Block.list from scratch, so every face is back on the fallback while
            // boundTextureId still names the id from the previous world - matching on that
            // alone found nothing, left the id stale, and no later atlas rebuild could
            // recover it either. That is why this scans for both and never short-circuits.
            int previous = boundTextureId;
            int touched = 0;
            for (int i = 0; i < Block.list.Length; i++)
            {
                Block block = Block.list[i];
                if (block == null || block.blockMaterial == null) continue;
                if (block.blockMaterial.id != AdamantGuard.MaterialId) continue;
                if (block.textureInfos == null) continue;

                for (int channel = 0; channel < block.textureInfos.Length; channel++)
                {
                    if (Replaceable(block.textureInfos[channel].singleTextureId, previous, to))
                    {
                        block.textureInfos[channel].singleTextureId = to;
                        touched++;
                    }
                    int[] sides = block.textureInfos[channel].sideTextureIds;
                    if (sides == null) continue;
                    for (int side = 0; side < sides.Length; side++)
                        if (Replaceable(sides[side], previous, to)) { sides[side] = to; touched++; }
                }
            }

            boundTextureId = to;
            if (touched > 0)
                Log.Out("[AdamantBlock] texture id " + to + " applied to "
                        + touched + " adamant block faces");
        }

        private static bool Replaceable(int id, int previous, int to)
        {
            return id != to && (id == previous || id == DonorTextureId);
        }

        private static void Destroy(Texture2DArray array)
        {
            if (array != null) UnityEngine.Object.Destroy(array);
        }

        // Takes a texture out of the texture-quality mipmap limit, so its mip 0 stays
        // resident and Graphics.CopyTexture will pair it with a Texture2DArray.
        private static void ExemptFromMipmapLimit(Texture2D tex)
        {
            if (tex != null) tex.ignoreMipmapLimit = true;
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

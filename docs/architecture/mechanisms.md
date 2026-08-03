# Architecture - the custom mechanisms

The mod combines three Harmony mechanisms and vanilla XML crafting, with no dependency on
any other mod. The first three mechanisms are independent and meet on the `adamantShapes`
block; the spike trap (mechanism 4) is a second block that reuses mechanisms 1 and 3 and
adds a Harmony hook of its own for its model.

## 1. Tool-vs-weapon damage gate (Harmony DLL)

Source: `src/dll/AdamantBlockMod.cs`, shipped as `AdamantBlock.dll`.

Two prefixes, on `Block.DamageBlock` and `Block.OnBlockDamaged`. `AdamantGuard.ShouldBlock`
decides per hit:

- Block material is not `MAdamant_shapes` → let the original run (no effect on other blocks).
- Source entity is not a player (zombie, animal, explosion, world) → block the hit.
- Source is a player → block only if the held item (`inventory.holdingItem`) has the vanilla
  `weapon` tag. Tools (`pickaxe`, `axe`, `shovel`, `auger`, salvage tools) lack it, so they
  mine normally; guns and melee weapons carry it, so they do zero damage.

Blocking a hit means setting `__result` to the current `BlockValue.damage` (unchanged) and
skipping the original method - no damage, no destroy, no impact ping.

Explosions are handled separately by material `explosionresistance="1"` in `materials.xml`,
not by the DLL.

Why the `weapon` tag and not `AttackHitInfo.WeaponTypeTag`: the latter only separates melee
from ranged (a pickaxe and a club both read as "melee"), so it cannot tell a tool from a
weapon. The item's own `weapon` tag can.

## 2. Custom texture in the block atlas (Harmony DLL)

Source: `src/dll/AdamantAtlas.cs`. Self-contained since 1.2.0 - this used to require the
OcbCustomTextures core mod.

The game builds the opaque block atlas like this (3.0.1, read off `Assembly-CSharp.dll`):

```
MeshDescription.LoadTextureArraysForQuality
  -> loadSingleArray x3                  ta_opaque[_n|_s]_<quality>.asset from Addressables
                                         -> MeshDescription.TexDiffuse/TexNormal/TexSpecular
  -> TextureAtlas.LoadTextureAtlas       copies those three refs into the atlas
     (TextureAtlasBlocks override runs LoadTextureAtlasFromMetadata first, which rebuilds
      TextureAtlas.uvMapping - the array a block's Texture property indexes into)
```

A **postfix on `TextureAtlasBlocks.LoadTextureAtlas`** is where the finished texture arrays and
a fresh `uvMapping` first exist together - a plain method, not a coroutine, so no transpiler is
needed. The first injection nevertheless waits for a **postfix on `Block.LateInitAll`**, because
config load order is `painting.xml` → `blocks.xml`: paint frameworks such as OcbCustomTextures
register their packs while `painting.xml` loads, and they compute where their own slices start
from the atlas as they find it. Growing the array before them shifts what their entries point
at - vanilla paints then wear our texture. Whoever extends the atlas **last** is the only one
who cannot corrupt anyone else's offsets, and `Block.LateInitAll` is also the first moment the
blocks that need the id exist. A later rebuild (texture-quality change) is re-applied from the
atlas postfix, where those frameworks have already run inside `loadSingleArray`.

Wherever it runs, the DLL:

1. allocates `depth + 1` copies of the three `Texture2DArray`s and blits every existing slice
   across with `Graphics.CopyTexture`;
2. pre-fills the new slice from the donor slice (texture id 356, steel), which guarantees a
   correctly formatted specular/MOER slice without shipping a third texture, then overwrites
   diffuse and normal with `Resources/adamant.unity3d?adamant_diffuse` / `?adamant_normal`;
3. points `TextureAtlas.diffuseTexture` / `normalTexture` / `specularTexture` at the new
   arrays **and only then** calls `MeshDescription.ReloadTextureArrays(false)`, which rebinds
   every chunk material (`mainTexture`, `_BumpMap`, `_MetallicGlossMap`, …) - it reads those
   atlas fields, not the `MeshDescription`, so rebinding first leaves the materials sampling
   the old array, where the new slice index is out of range. An out-of-range slice **clamps
   to the last one** instead of erroring, which looks like the block wearing a random other
   texture. The result is verified and logged rather than assumed;
4. appends one `UVRectTiling` record - cloned from the donor, retargeted at the new slice as
   a full 1×1 tile - and takes its array index as the new texture id;
5. rewrites that id onto every block whose material is `MAdamant_shapes`, replacing the
   fallback 356 from `blocks.xml`;
6. releases the arrays it replaced through `MeshDescription.Unload` - **last**, once nothing
   references them any more.

Mesh and atlas are resolved live (`MeshDescription.meshes[cIndexOpaque].textureAtlas`) at the
moment of injection, never from what the atlas postfix saw: rendering reads
`MeshDescription.meshes[MeshIndex].textureAtlas.uvMapping` (`BlockShapeNew.renderFace`), and
appending to any other instance leaves the block with an id past the end of the live array.

The whole path is idempotent, so a texture-quality change mid-game simply re-runs it.

**No paint entry is registered on purpose.** A `<paint>`/`BlockTextureData` record would put
the texture into the paint tool, but paint ids are persisted per painted face in the save and
dynamically assigned ones drift when the set of installed paint mods changes - the failure
mode behind `Missing paint ID XML entry: N for block …`. A block's own texture id lives in
`blocks.xml` and is re-resolved every start, so staying out of that id space keeps saves safe.
The price is that adamant cannot be selected in the paint tool.

Texture format is strict, because `Graphics.CopyTexture` needs an exact match: both textures
512², diffuse DXT1/BC1 without alpha, normal DXTnm. On texture quality 1 the atlas is loaded
at half size, so the injector copies from the matching **mip level** of the source instead of
mip 0. If anything does not line up - missing bundle, wrong format, dedicated server with no
textures at all - it logs the reason and leaves the block on texture 356 (steel).

Size and format are not the whole story: `CopyTexture` also refuses a pair whose **mipmap
limits** differ, and lowering Texture Quality puts every loaded `Texture2D` at a non-zero
limit while a `Texture2DArray` is always at 0. The two bundle textures and the generated
surface-response texture are therefore held at `ignoreMipmapLimit = true`, in the importer
and again in code. Without it the copies are all rejected - with nothing but Unity's own
`different mipmap limits` line to show for it - and the slice keeps the steel it was
pre-filled with, which is indistinguishable from a vanilla block. That was the 1.2.2 bug.

The failure path is deliberately loud and total: an abort is a `Log.Error`, the half-filled
array is thrown away rather than published, and the blocks are put back on 356 - never left
on an id from a previous atlas, which a rebuilt `uvMapping` may be too short to contain.

## 3. Survival crafting chain

Three XML files, survival edition only:

- `Config/items.xml` - `adamantOre` (extends `resourceCoal`) and `adamantIngot` (extends
  `resourceForgedSteel`), each with a `CustomIcon` and a `DescriptionKey`.
- `Config/blocks.xml` - appends a rare `adamantOre` Harvest drop (`prob="0.04"`) to
  `terrOreIron`, `terrOreLead`, `terrOreCoal`.
- `Config/recipes.xml` - `adamantIngot` at the **workbench** from
  `25 resourceForgedSteel + 20 resourceConcreteMix + 10 resourceScrapPolymers + 5 resourceScrapLead + 1 adamantOre`;
  `adamantShapes:VariantHelper` at the workbench from `1 adamantIngot`.

**The ingot recipe must not be a forge recipe** - this was a real bug. The forge block
declares `Modules="tools,output,fuel,material_input"` with
`InputMaterials="iron,brass,lead,glass,stone,clay"`, and its
`XUiC_WorkstationMaterialInputGrid` *inherits* from `XUiC_WorkstationInputGrid`. The crafting
code's `GetChildByType<XUiC_WorkstationInputGrid>()` therefore finds it and resolves every
ingredient against the molten-material pool, which can only hold those six materials. With
ordinary items as ingredients that produces two symptoms: `calcMaxCraftable` counts 0 and then
does `Mathf.Clamp(0, 1, 10000)`, so the craft count sticks at **1**; and
`ItemActionEntryCraft` validates with `PlayerInventory.HasItems(...) || grid.HasItems(...)`
(the backpack satisfies it) but deducts with `if (grid != null) grid.RemoveItems(...)`, so
**nothing is consumed**. That is why every vanilla forge recipe ships as a
`use_smelter` (`unit_*`, `material_based="true"`) / `replace_smelter` (real items) pair.

A workstation whose `Modules` are output-only - the workbench - resolves and deducts against
the player inventory, which is what recipes with arbitrary item ingredients need. No
progression is lost by moving off the forge: `resourceForgedSteel` is itself a forge recipe
with `craft_tool="toolForgeCrucible"`, so the 25 steel already gate the ingot behind a
steel-tier forge.

The creative edition replaces `recipes.xml` with a single `1 resourceWood → block` recipe and
ships none of the above.

## 4. Adamant Spikes Trap

A second block, `adamantSpikesTrap`, in `Config/blocks.xml` of both editions. Its *behavior* is
pure XML - it reuses the `MAdamant_shapes` material, and mechanism 1 matches on the material
id, so the damage gate covers it for free. Its *look* needs the DLL (see below).

`Class="TrunkTip"` is the vanilla spike-trap class (`BlockTrunkTip : BlockDamage`). Its
constructor sets `IsCheckCollideWithEntity`, which is the flag `ChunkCluster.CheckCollisionWithBlocks`
requires before it will call `OnEntityCollidedWithBlock` at all.

Three properties carry the behavior:

- **`Damage="60"`** - 1.8× the vanilla iron spike (33). One hit lands per 30 game ticks;
  `GameTimer` is constructed with `ticksPerSecond = 20f`, so that is exactly 1.5 s (~40 DPS).
  That cooldown lives on the *victim* and is keyed by `EnumDamageSource.External`, which every
  `BlockDamage` block shares - so **stacking spike blocks does not stack damage**. Extra blocks
  buy exposure time through the slow, nothing else.
- **`Damage_received="0"`** - no self-damage. `OnEntityCollidedWithBlock` only calls
  `DamageBlock` when the computed block damage is `> 0`. For contrast, an iron spike takes 33
  per hit against `MaxDamage=200`, dies after 6 hits, and so has a lifetime output of ~198
  damage for 4 forged iron. Removing that cap is the real balance change, not the damage number.
- **no `BlockTag="Spike"`** - deliberate omission. An IL sweep over every `Block::HasTag` call
  shows `BlockTags.Spike` (6) is read in exactly one place, `EntityAlive.CalculateBlockDamage`,
  where `StompsSpikes` entities (only `zombieDemolition`) deal 999 damage *with*
  `bypassMaxDamage`. Omitting the tag is therefore free and makes the trap demolisher-proof.

`MovementFactor="0.25"` is a block-level property that overrides the material's
`movement_factor`; it is intentionally weaker than the iron spike's 0.18 so adamant trades
holding power for damage and permanence rather than being better on every axis. It slows the
player too.

### Look: the model is retextured, not tinted

Visuals reuse the vanilla prefab `@:Entities/Traps/ironSpikesTrapPrefab.prefab`. The atlas
texture from mechanism 2 does not reach it - a `ModelEntity` block gets its material from the
prefab, not from the block atlas - so `src/dll/AdamantTrapModel.cs` swaps the texture that
material samples instead.

**`TintColor` was tried first and does not work here.** Two findings, both verified against
3.0.1 `Assembly-CSharp.dll`:

- The world path for a `ModelEntity` block is **not** `BlockShapeModelEntity.CloneModel` (an
  earlier version of this document and of the `blocks.xml` comment said so and was wrong). It
  is `BlockShapeModelEntity.OnBlockEntityTransformBeforeActivated`, which ends in
  `BlockEntityData.SetMaterialColor("_Color", tintColor)` - a `MaterialPropertyBlock` carrying
  a colour the shader *multiplies* onto the albedo. `CloneModel` covers the other instantiation
  sites and additionally sets a `TintColor` property.
- **This prefab's shader does not declare `_Color` at all**, so that write was a no-op, not a
  bad colour. Its full property list, logged in game: `_Tint [Float]`, `_Cutoff`,
  `_EmissionMultiply`, `_MainTex`, `_Normal`, `_Emissive`, `_RMOM`, `_MacroAO` - the tint knob
  here is a **float** named `_Tint`. A `MaterialPropertyBlock` silently ignores properties the
  shader does not declare, which is why nothing happened and nothing was logged.
- The general rule behind that: `TintColor` is an albedo *multiplier* and only yields the
  intended colour on a pale albedo authored for it. Vanilla sets it exclusively on gun safes,
  munition boxes and chests. Nothing under `Entities/Traps` uses it, and `ironSpikesTrapPrefab`
  draws from a single rust-brown metal material
  (`Entities/Traps/Materials/ironSpikesTrap.mat`, texture `ironSpikesTrap.tga`) - so even on a
  shader that did honour `_Color`, `#7B4FB0` over that would give dark mud, not adamant.

The replacement clones that material once, points the clone's albedo/normal slots at the
`adamant_diffuse` / `adamant_normal` the mod already ships for the atlas, and assigns it to the
instantiated renderers via two postfixes - on `OnBlockEntityTransformBeforeActivated` (placed
blocks) and on `CloneModel` (held item, previews). Notes on the implementation:

- **The prefab and its material asset are never written to**, only clones are - so the vanilla
  iron spikes trap keeps its own look.
- **`sharedMaterials`, not `materials`**: reading `.materials` instantiates a private copy per
  renderer, and the engine pools these model GameObjects. One clone is cached per distinct
  source material; a `HashSet` of the clones' instance ids stops a pooled renderer that already
  carries ours from being cloned again on reuse.
- **Three slots, not one.** Confirmed in game: the material is `ironSpikesTrap` on shader
  **`Game/Entity Tint Mask`**, exposing `_MainTex`, `_Normal`, `_Emissive`, `_RMOM`. The first
  version probed `_BumpMap`/`_NormalMap` and so applied the albedo only - flat colour, no
  relief, and the iron spike's own rusty near-matte surface map still in place ("the gloss is
  missing"). `_Normal` takes `adamant_normal`; `_RMOM` gets a generated uniform surface with
  the same physical values as the block's atlas slice (metallic 0.7, AO 0.9, emission 0,
  roughness 0.35), 2×2 RGBA32, linear, no file needed.
  ⚠ The atlas channel is **MOER**-ordered, this slot spells **RMOM** in its own name, so the
  constant is re-ordered (`0.35, 0.7, 0.9, 0`). Alpha 0 is safe whether the trailing `M` means
  emissive or tint mask.
  ⚠ **That ordering is read off the property name, not proven.** The shader's description for
  the slot is the bare string `"RMOM"` - it does not spell the channels out, the shader bundle
  is LZ4-compressed, and no Managed DLL names the property. It is confirmed only by how the
  trap looks in game (3.0.1, 2026-07-31). If a future build looks matte and non-metallic,
  swapping R and G in `SurfaceRMOM` is the first thing to try.
- Slot names are probed through `Material.HasProperty`, and the material's shader plus every
  texture/float property **with its description** is logged once - packed maps spell their
  channel order in the description, and that is the only proof available offline (the shader
  bundle is compressed and no Managed DLL names `_RMOM`). The slots actually written are named
  in the log too; a count alone hid the missing normal map the first time round.
- No bundle (dedicated server, missing or malformed `adamant.unity3d`) means the trap keeps the
  vanilla iron look, matching how the atlas injector degrades.

`CustomIconTint="7B4FB0"` stays: the inventory icon is a different code path and does tint.

Cost is 2 `adamantIngot` at the workbench with 1 refunded on harvest, a 50% relocation tax.
**The block itself is never returned**, verified rather than assumed: `Block.PickupOrDrop`
returns early unless `Block.CanPickup` (not set here), or `forcePickup`, or
`EffectManager.GetValue(PassiveEffects.BlockPickup /*173*/, …, block.Tags) > 0` - and vanilla
grants that effect only for the tags `Mine1`-`Mine4` (the Perception trap perk, four landmine
blocks). The trap carries no `Tags` at all. The 1 ingot is the *base* Harvest yield; the drop
uses the standard vanilla `tag="allHarvest,perkJunkMiner"` (692 vanilla uses), so harvest perks
and `XUiM_Recipes.HarvestingOutputModifier` scale it like any other block drop.
The creative edition uses `1 resourceWood` for both the recipe and the repair/harvest entries,
since it ships no ingot.

## File-to-mechanism map

| File | Mechanism |
|---|---|
| `AdamantBlock.dll` + `src/dll/AdamantBlockMod.cs` | tool-vs-weapon gate |
| `src/dll/AdamantTrapModel.cs` | spikes-trap model retexture |
| `Config/materials.xml` | stability + explosion immunity |
| `Config/painting.xml` + `Resources/adamant.unity3d` | custom texture |
| `Config/blocks.xml` | block definitions (`adamantShapes`, `adamantSpikesTrap`) + ore drops |
| `Config/items.xml` + `UIAtlases/ItemIconAtlas/` | ore/ingot items + icons |
| `Config/recipes.xml` | crafting chain (edition-specific) |
| `Config/Localization.csv` | names + descriptions, 13 languages |

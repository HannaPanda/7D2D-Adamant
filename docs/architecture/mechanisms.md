# Architecture - the custom mechanisms

The mod combines a Harmony DLL, an OcbCustomTextures paint entry, and vanilla XML
crafting. The first three mechanisms are independent and meet on the `adamantShapes`
block; the spike trap (mechanism 4) is a second block that reuses mechanisms 1 and 3.

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

## 2. Custom texture via OcbCustomTextures

`Config/painting.xml` adds `<opaque id="adamant">` under `/paints`. The OcbCustomTextures
core mod reads it and injects a new slice into the opaque block texture array, which makes
`adamant` a first-class atlas entry usable by the full `shapes="All"` set and by the paint
tool.

The entry points `Diffuse` and `Normal` at `Resources/adamant.unity3d?adamant_diffuse` and
`?adamant_normal` (512², DXT1 and DXTnm). `Specular` is an on-the-fly uniform MOER string
(`512:512:0.7:0.9:0:0.35`), so no third texture ships. The block sets `Texture="adamant"`
and keeps `Shape="New"` / `shapes="All"`.

Without OcbCustomTextures installed the block still loads, but the texture is missing.

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

A second block, `adamantSpikesTrap`, in `Config/blocks.xml` of both editions. Pure XML - it
needs no DLL change because it reuses the `MAdamant_shapes` material, and mechanism 1 matches
on the material id.

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

Visuals reuse the vanilla prefab `@:Entities/Traps/ironSpikesTrapPrefab.prefab` recolored by
`TintColor="7B4FB0"` (the adamant purple from `src/texture/gen_adamant_512.py`).
`BlockShapeModelEntity.CloneModel` applies the tint whenever its alpha is `> 0`, so this needs
no Unity work. The atlas texture from mechanism 2 does not apply here - a `ModelEntity` block
gets its material from the prefab.

Cost is 2 `adamantIngot` at the workbench with 1 refunded on harvest, a 50% relocation tax.
The creative edition uses `1 resourceWood` for both the recipe and the repair/harvest entries,
since it ships no ingot.

## File-to-mechanism map

| File | Mechanism |
|---|---|
| `AdamantBlock.dll` + `src/dll/AdamantBlockMod.cs` | tool-vs-weapon gate |
| `Config/materials.xml` | stability + explosion immunity |
| `Config/painting.xml` + `Resources/adamant.unity3d` | custom texture |
| `Config/blocks.xml` | block definitions (`adamantShapes`, `adamantSpikesTrap`) + ore drops |
| `Config/items.xml` + `UIAtlases/ItemIconAtlas/` | ore/ingot items + icons |
| `Config/recipes.xml` | crafting chain (edition-specific) |
| `Config/Localization.csv` | names + descriptions, 13 languages |

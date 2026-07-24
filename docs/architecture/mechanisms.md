# Architecture — the three custom mechanisms

The mod combines a Harmony DLL, an OcbCustomTextures paint entry, and vanilla XML
crafting. Each mechanism is independent; they only meet on the `adamantShapes` block.

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
skipping the original method — no damage, no destroy, no impact ping.

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

- `Config/items.xml` — `adamantOre` (extends `resourceCoal`) and `adamantIngot` (extends
  `resourceForgedSteel`), each with a `CustomIcon` and a `DescriptionKey`.
- `Config/blocks.xml` — appends a rare `adamantOre` Harvest drop (`prob="0.04"`) to
  `terrOreIron`, `terrOreLead`, `terrOreCoal`.
- `Config/recipes.xml` — `adamantIngot` at the forge (crucible required) from
  `25 resourceForgedSteel + 20 resourceConcreteMix + 10 resourceScrapPolymers + 5 resourceScrapLead + 1 adamantOre`;
  `adamantShapes:VariantHelper` at the workbench from `1 adamantIngot`.

The creative edition replaces `recipes.xml` with a single `1 resourceWood → block` recipe and
ships none of the above.

## File-to-mechanism map

| File | Mechanism |
|---|---|
| `AdamantBlock.dll` + `src/dll/AdamantBlockMod.cs` | tool-vs-weapon gate |
| `Config/materials.xml` | stability + explosion immunity |
| `Config/painting.xml` + `Resources/adamant.unity3d` | custom texture |
| `Config/blocks.xml` | block definition + ore drops |
| `Config/items.xml` + `UIAtlases/ItemIconAtlas/` | ore/ingot items + icons |
| `Config/recipes.xml` | crafting chain (edition-specific) |
| `Config/Localization.csv` | names + descriptions, 13 languages |

# Adamant Block - 7 Days to Die (V2.6, V3.0 / V3.1)

A near-indestructible building block with a custom purple crystalline texture.

- Takes **zero damage** from zombies, animals, explosions, and player **weapons**
  (guns *and* melee weapons).
- Can **only be mined with tools** (pickaxe, axe, auger, shovel, nailgun, salvage tools).
- **10× steel** stability (`stability_glue` 3000), full explosion immunity.
- Custom **`adamant`** opaque texture that works on the **full shape set**
  (`shapes="All"`), injected into the block atlas by the mod's own DLL - **no core
  mod required**.
- Custom item icons for the ore and ingot; localized into **13 languages**
  (EN, DE, ES, FR, IT, JA, KO, PL, PT-BR, RU, TR, ZH-Hans, ZH-Hant).

Two editions are provided (install **one**):

| Edition | Folder | Crafting |
|---|---|---|
| **Survival** (main) | `AdamantBlock/` | Rare **Adamant Ore** → **Adamant Ingot** → block (workbench) |
| **Creative** (optional) | `AdamantBlock-Creative/` | 1 wood → block, straight from the backpack |

## Compatibility

Tested on **V 2.6 (b14)**, **V 3.0.0 (b259)**, **V 3.0.1 (b4)** and **V 3.1.0 (b14)**.

Only game versions that were actually launched and verified are listed here. Other
builds may well work - the mod is XML plus a handful of Harmony patches - but they are untested,
and the Harmony patches are the part that can break silently on a new build. The tested
list is re-established for every mod release; see `docs/build-and-release.md`.

## Requirements

- **No other mods.** The texture is added to the block atlas by `AdamantBlock.dll`
  itself; if that ever fails (dedicated server, damaged bundle) the block falls back
  to the vanilla steel texture and stays fully playable.
- **EasyAntiCheat must be OFF** - this mod ships a Harmony DLL
  (`SkipWithAntiCheat` is set). Works in single-player and on private servers.
- **Multiplayer:** install on **both client and server**.

## Installation

1. Copy **one** edition's folder (`AdamantBlock` *or* `AdamantBlock-Creative`)
   into your `7 Days To Die/Mods/` folder.
2. Launch with EAC disabled.

## Survival progression

1. **Adamant Ore** - small chance (`prob 0.04`) to drop while harvesting deep
   ore veins (`terrOreIron` / `terrOreLead` / `terrOreCoal`).
2. **Adamant Ingot** - crafted at the **workbench**:
   `25× Forged Steel + 20× Concrete Mix + 10× Scrap Polymers + 5× Scrap Lead + 1× Adamant Ore`.
   The crucible gate is indirect - `resourceForgedSteel` is itself a forge recipe that
   requires one.
3. **Adamant Block** - crafted at the **workbench** from `1× Adamant Ingot`.
4. **Adamant Spikes Trap** - crafted at the **workbench** from `2× Adamant Ingots`.

Tuning: raise the ore `prob` in `blocks.xml`, or bump the block recipe `count`
in `recipes.xml`, to taste.

## Adamant Spikes Trap

A second block, crafted at the workbench from **2 Adamant Ingots** (creative edition:
1 wood, from the backpack).

- **60 damage per hit**, against 33 for vanilla iron spikes.
- **Never wears out** - it takes no damage from hurting things, where iron spikes break
  after about six hits.
- Same protection as the block: immune to zombies, weapons and explosions, and
  **demolisher-proof** (it deliberately carries no `BlockTag="Spike"`, which is the only
  thing `zombieDemolition` checks before stomping a trap for 999).
- Slows whatever stands in it (`MovementFactor="0.25"`) - the player included.

**Mining it returns 1 Adamant Ingot, not the trap itself.** The block cannot be picked up
whole: it sets no `CanPickup`, and the only other route into `Block.PickupOrDrop` is the
`BlockPickup` passive effect, which vanilla grants for the `Mine1`-`Mine4` tags only
(landmines). So relocating a trap costs you one of the two ingots. That 1 ingot is the
*base* harvest yield - the drop carries the standard vanilla `allHarvest,perkJunkMiner`
tag, so harvest perks and the world's loot/harvest abundance setting scale it like any
other block drop.

Traps share a 1.5 s damage cooldown **per target**, not per block, so stacking more spikes
does not multiply damage - a deeper field buys exposure time, not DPS.

## How it works

- **Tool-vs-weapon gate** - a Harmony patch (`AdamantBlock.dll`) on
  `Block.DamageBlock` / `OnBlockDamaged`. Non-player damage is always blocked;
  for a player it blocks the hit only when the held item carries the vanilla
  `weapon` tag (tools lack it). See `src/dll/AdamantBlockMod.cs`.
- **Custom texture** - a Harmony postfix on `TextureAtlasBlocks.LoadTextureAtlas`
  grows the opaque block texture arrays by one slice, fills it from
  `Resources/adamant.unity3d` (512² `adamant_diffuse` DXT1 + `adamant_normal`
  DXTnm) and hands the resulting atlas index to every adamant block. No paint
  entry is created, so no paint id ends up in your save.
  See `src/dll/AdamantAtlas.cs` and `docs/architecture/mechanisms.md`.
- **Spikes-trap look** - the trap reuses the vanilla iron-spike model, which is a
  `ModelEntity` block and therefore gets its material from the prefab, not from the block
  atlas. Two postfixes on `BlockShapeModelEntity` clone that material once and point its
  albedo/normal/surface slots at the mod's own textures. The prefab is never modified, so
  the vanilla iron spikes trap keeps its own look. See `src/dll/AdamantTrapModel.cs`.

## Building from source

**DLL** (`src/dll/`): references the game's `Assembly-CSharp.dll`, `0Harmony.dll`,
`UnityEngine*.dll`. Build with the .NET SDK:
```
DOTNET_ROLL_FORWARD=LatestMajor dotnet build -c Release -o out
```
Copy `AdamantBlock.dll` into each edition's mod folder.

**Texture bundle** (`src/texture/`): open the two 512² PNGs in Unity **2022.3.62f2**
(Built-in pipeline, Linear color space). Diffuse = `RGB Compressed DXT1`,
Alpha Source None; Normal = Texture Type *Normal map*, `RGBA Compressed DXT5`.
Assign AssetBundle `adamant` / variant `unity3d` to both, drop `BuildBundles.cs`
into `Assets/Editor`, then **7DTD ▸ Build Adamant Bundle**. Place the resulting
`adamant.unity3d` in `Resources/`. Regenerate the source textures with
`gen_adamant_512.py` (numpy + Pillow).

## Credits

- Harmony by pardeike.

## License

MIT - see [LICENSE](LICENSE).

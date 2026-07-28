# Adamant Block - 7 Days to Die (V3.0)

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
| **Survival** (main) | `AdamantBlock/` | Rare **Adamant Ore** → forge-smelted **Adamant Ingot** → block |
| **Creative** (optional) | `AdamantBlock-Creative/` | 1 wood → block, straight from the backpack |

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
2. **Adamant Ingot** - smelted at the **forge** (crucible required):
   `25× Forged Steel + 20× Concrete Mix + 10× Scrap Polymers + 5× Scrap Lead + 1× Adamant Ore`.
3. **Adamant Block** - crafted at the **workbench** from `1× Adamant Ingot`.

Tuning: raise the ore `prob` in `blocks.xml`, or bump the block recipe `count`
in `recipes.xml`, to taste.

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

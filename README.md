# Adamant Block — 7 Days to Die (V3.0)

A near-indestructible building block with a custom purple crystalline texture.

- Takes **zero damage** from zombies, animals, explosions, and player **weapons**
  (guns *and* melee weapons).
- Can **only be mined with tools** (pickaxe, axe, auger, shovel, nailgun, salvage tools).
- **10× steel** stability (`stability_glue` 3000), full explosion immunity.
- Custom **`adamant`** opaque texture that works on the **full shape set**
  (`shapes="All"`) and is selectable in the **paint tool**.

Two editions are provided (install **one**):

| Edition | Folder | Crafting |
|---|---|---|
| **Survival** (main) | `AdamantBlock/` | Rare **Adamant Ore** → forge-smelted **Adamant Ingot** → block |
| **Creative** (optional) | `AdamantBlock-Creative/` | 1 wood → block, straight from the backpack |

## Requirements

- **[OcbCustomTextures](https://www.nexusmods.com/7daystodie/mods/2788)** — **required.**
  Injects the custom `adamant` texture into the block atlas. Without it the block
  still works but renders with a missing/placeholder texture.
- **EasyAntiCheat must be OFF** — this mod ships a Harmony DLL
  (`SkipWithAntiCheat` is set). Works in single-player and on private servers.
- **Multiplayer:** install on **both client and server**.

## Installation

1. Install **OcbCustomTextures** (see above).
2. Copy **one** edition's folder (`AdamantBlock` *or* `AdamantBlock-Creative`)
   into your `7 Days To Die/Mods/` folder.
3. Launch with EAC disabled.

## Survival progression

1. **Adamant Ore** — small chance (`prob 0.04`) to drop while harvesting deep
   ore veins (`terrOreIron` / `terrOreLead` / `terrOreCoal`).
2. **Adamant Ingot** — smelted at the **forge** (crucible required):
   `25× Forged Steel + 20× Concrete Mix + 10× Scrap Polymers + 5× Scrap Lead + 1× Adamant Ore`.
3. **Adamant Block** — crafted at the **workbench** from `1× Adamant Ingot`.

Tuning: raise the ore `prob` in `blocks.xml`, or bump the block recipe `count`
in `recipes.xml`, to taste.

## How it works

- **Tool-vs-weapon gate** — a Harmony patch (`AdamantBlock.dll`) on
  `Block.DamageBlock` / `OnBlockDamaged`. Non-player damage is always blocked;
  for a player it blocks the hit only when the held item carries the vanilla
  `weapon` tag (tools lack it). See `src/dll/AdamantBlockMod.cs`.
- **Custom texture** — an `<opaque>` paint entry (`Config/painting.xml`) consumed
  by OcbCustomTextures, backed by `Resources/adamant.unity3d`
  (512² `adamant_diffuse` DXT1 + `adamant_normal` DXTnm).

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

- **OcbCustomTextures** by OCB (Marc Streckfuß) — custom block texture framework.
- Harmony by pardeike.

## License

MIT — see [LICENSE](LICENSE).

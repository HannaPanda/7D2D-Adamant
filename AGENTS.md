# AGENTS.md - Adamant Block (7 Days to Die V3.0 mod)

Onboarding for AI agents working on this repo. Read this first.

## What this project is

A near-indestructible building block for **7 Days to Die V3.0** with three custom
mechanisms and two shippable editions.

- **Tool-vs-weapon damage gate** (Harmony DLL): only tools mine it; weapons, zombies,
  animals and explosions deal zero damage.
- **Custom purple crystalline texture** on the full `shapes="All"` set, via the
  **OcbCustomTextures** core mod (required dependency).
- **Survival crafting chain**: rare Adamant Ore → forge-smelted Adamant Ingot → block.

## Repository layout

| Path | What |
|---|---|
| `AdamantBlock/` | **Survival** edition - the deployable 7DTD mod (ModInfo.xml, Config/, Resources/adamant.unity3d, AdamantBlock.dll, UIAtlases/) |
| `AdamantBlock-Creative/` | **Creative** edition - same, but recipe = 1 wood, no ore/ingot/icons |
| `src/dll/` | C# Harmony source + `.csproj` |
| `src/texture/` | Texture + icon sources and Python generators |
| `.github/workflows/release.yml` | CI: tag `v*` → build zips → GitHub Release |

Editions are alternatives - a user installs **one**. They share block/material/texture/DLL
and differ only in `recipes.xml` (+ the survival-only `items.xml`, ore drops, icons,
extra localization rows).

## Docs map

Detailed documentation lives in `docs/`:

- [`docs/architecture/mechanisms.md`](docs/architecture/mechanisms.md) - how the three
  custom mechanisms (tool-vs-weapon DLL, OcbCustomTextures texture, survival crafting chain)
  are wired, with a file-to-mechanism map.
- [`docs/conventions/modding.md`](docs/conventions/modding.md) - verified vanilla item names,
  the Localization.csv RFC-CSV quoting rule, the item-icon convention, and the path/tooling
  gotchas.
- [`docs/build-and-release.md`](docs/build-and-release.md) - building the DLL and the Unity
  bundle, regenerating art/localization, the tag-driven CI release, and the manual Nexus upload.

Keep these in sync with the code: any change to behavior, structure or conventions updates the
matching `docs/` file in the same commit.

## Environment (this machine)

- **Game**: `C:\Steam\steamapps\common\7 Days To Die` - `Assembly-CSharp.dll` under
  `7DaysToDie_Data\Managed\`, vanilla config under `Data\Config\`.
- **Live deployment** (MO2 "Smorgasbord" modlist):
  `C:\Modlists\Smorgasbord\mods\[NoDelete]Adamant Block\AdamantBlock\`
  - this is the running copy; the repo's `AdamantBlock/` mirrors it. **Keep them in sync.**
- **Unity** (for the texture bundle only): `2022.3.62f2`, Built-in pipeline, Linear color space.
- **Dependency**: **OcbCustomTextures** (Nexus mod 2788) - installed in the modlist,
  required for the texture to render.

## How the three mechanisms are wired

1. **Tool-vs-weapon** - `src/dll/AdamantBlockMod.cs`: Harmony prefix on
   `Block.DamageBlock` / `Block.OnBlockDamaged`. Blocks the hit when the source is not a
   player, or when the player's held item (`inventory.holdingItem`) carries the vanilla
   `weapon` tag (tools lack it). Material `explosionresistance="1"` handles explosions.
2. **Texture** - `Config/painting.xml` defines `<opaque id="adamant">` (consumed by
   OcbCustomTextures) pointing at `Resources/adamant.unity3d?adamant_diffuse` /
   `?adamant_normal` (512², DXT1 + DXTnm) plus an on-the-fly Specular string. The block
   sets `Texture="adamant"` and keeps `shapes="All"`.
3. **Survival chain** - `Config/items.xml` (`adamantOre`, `adamantIngot`, tinted/iconed),
   `Config/recipes.xml` (forge ingot + workbench block), `Config/blocks.xml` (rare
   `adamantOre` Harvest drop on `terrOreIron/Lead/Coal`).

## Common tasks

- **Change a recipe** → `AdamantBlock/Config/recipes.xml`. Verified vanilla item names:
  `resourceForgedSteel`, `resourceConcreteMix`, `resourceScrapPolymers`,
  `resourceScrapLead` (NOT `resourceLead`), `resourceWood`.
- **Add/fix a translation** → `AdamantBlock/Config/Localization.csv`. 13 language columns.
  **Must be RFC-CSV**: any value containing a comma has to be quoted. Regenerate with
  `python src/texture/gen_localization.py` to guarantee correct quoting; file is UTF-8.
- **Item icons** → `UIAtlases/ItemIconAtlas/<name>.png`, **160×160 RGBA**, referenced by
  `<property name="CustomIcon" value="<name>"/>`. Hi-res originals: `src/texture/*_source.png`.
- **Rebuild the DLL** → `cd src/dll && DOTNET_ROLL_FORWARD=LatestMajor dotnet build -c Release -o out`.
  Needs the game DLLs referenced by absolute path in the `.csproj`. Copy
  `out/AdamantBlock.dll` into **both** editions.
- **Rebuild the texture bundle** → Unity 2022.3.62f2 (see `README.md`). **Not possible in CI.**
- **Test in-game** → copy `AdamantBlock/` contents into the MO2 live path above, then launch.
  XML/bundle load at **startup** only.

## Release flow

1. Bump `<Version>` in **both** `ModInfo.xml`, update `CHANGELOG.md`.
2. `git tag vX.Y.Z && git push origin vX.Y.Z`.
3. CI validates XML, checks installable structure, builds MO2/Vortex zips (mod folder at
   zip root, forward slashes) and publishes a GitHub Release with both zips.
- CI **cannot** build `AdamantBlock.dll` (needs the game's `Assembly-CSharp.dll`) or
  `adamant.unity3d` (needs Unity) - those binaries stay committed. **Do not remove them.**
- **Nexus upload is manual** - there is no upload API/CLI. Main file = Survival zip,
  optional = Creative zip. List OcbCustomTextures as a requirement; disclose "contains DLL"
  and "EAC must be off".

## Gotchas (all verified on this machine)

- The MO2 path contains `[NoDelete]` - PowerShell treats `[]` as wildcards; use
  `-LiteralPath`. In Python use the Windows form `C:/Modlists/...`, not `/c/...`.
- **Unquoted commas in Localization.csv silently break parsing** (text truncates at the
  first comma, later columns shift).
- **PowerShell 5.1 `Compress-Archive` writes backslash zip paths** (malformed per ZIP
  spec) - build release zips with `zip` (CI) or Python `zipfile`, never Compress-Archive.
- A **running game locks `AdamantBlock.dll`** → overwrite fails with "Permission denied".
  Close 7DTD before deploying the DLL.
- Lead item is `resourceScrapLead`.

## Deeper 7DTD knowledge

Verified engine/API notes (block-damage internals, OcbCustomTextures API, tag mechanics,
icon/CSV conventions) live in the user's `7d2d-modding` Claude skill `LEARNINGS.md`
(outside this repo). Interrogate `Assembly-CSharp.dll` with that skill's `dump-*` scripts
rather than guessing engine behavior.

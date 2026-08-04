# AGENTS.md - Adamant Block (7 Days to Die V2.6 / V3.0 / V3.1 mod)

Onboarding for AI agents working on this repo. Read this first.

## What this project is

A near-indestructible building block for **7 Days to Die V2.6 and V3.0 / V3.1** with four custom
mechanisms and two shippable editions.

- **Tool-vs-weapon damage gate** (Harmony DLL): only tools mine it; weapons, zombies,
  animals and explosions deal zero damage.
- **Custom purple crystalline texture** on the full `shapes="All"` set, injected into the
  opaque block atlas by our own DLL - **no dependencies** (this replaced the
  OcbCustomTextures requirement in 1.2.0).
- **Survival crafting chain**: rare Adamant Ore → Adamant Ingot → block, both at the workbench.
- **Adamant Spikes Trap**: an indestructible, non-degrading spike trap. Behavior is pure
  XML - it reuses the material, so the DLL gate covers it for free; the vanilla spike model
  is retextured with the mod's own texture by the DLL (`TintColor` cannot do it, see
  `docs/architecture/mechanisms.md`).

New content goes **into this mod**, not into a companion mod: 7DTD 3.0.1 has no
dependency system at all (`Mod`/`ModInfo` expose only Name/DisplayName/Description/
Author/Version/Website), and a block referencing a missing material makes
`BlocksFromXml.CreateBlock` throw, which aborts the whole blocks.xml load. Config file
names are also fixed by the engine (`XmlPatcher.LoadAndPatchConfig` looks for
`<mod>/Config/<vanilla-config-name>.xml`), so splitting buys no file organization
either - separate features with comment banners inside the existing files. The one safe
companion-mod shape is a patch that *only* does `<set>`/`<remove>` on existing elements:
a non-matching xpath merely warns instead of throwing.

## Version compatibility - the rule

**Never claim "works on 3.x".** Name only the game versions this mod release was actually
launched on with its log checked. Currently: **2.6, 3.0.0, 3.0.1 and 3.1.0** (each verified in two
tiers - headless smoke test plus a GUI run for the graphical path).

The list is per mod version and lives in four places that must stay in sync - the
`TESTED_VERSIONS` env var in `.github/workflows/release.yml` (which feeds the GitHub release
body and both Nexus file descriptions), the *Compatibility* section of `README.md`, and the
Requirements list in `nexus/description.bbcode`. Re-establish it with the headless test bench
before every release; procedure in [`docs/build-and-release.md`](docs/build-and-release.md).
Pure XML changes may ride on a smoke test; **any DLL/Harmony change invalidates the whole
list**.

**The GUI pass has a second axis: Texture Quality.** Run it at **Full, Half and Quarter**, and
look at a placed block on each. The game rebuilds the whole opaque atlas when that setting
changes - different size, different mip count, and a different mipmap limit on every loaded
texture - so the injection in `AdamantAtlas.cs` runs down a different path per level. 1.2.2
shipped a block that rendered as plain vanilla steel on anything below Full, and it went
unnoticed for a release because the GUI pass only ever ran on the developer machine's own
setting. Switching the setting mid-session is enough; a restart is not required.

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
  custom mechanisms (tool-vs-weapon DLL, atlas texture injection, spikes-trap model
  retexture, survival crafting chain)
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
- **Dependencies: none.** OcbCustomTextures is still installed in the modlist for *other*
  mods, but Adamant no longer uses it and must keep working with it absent - the two do not
  interact (we never touch the paint id space it manages).

## How the mechanisms are wired

1. **Tool-vs-weapon** - `src/dll/AdamantBlockMod.cs`: Harmony prefix on
   `Block.DamageBlock` / `Block.OnBlockDamaged`. Blocks the hit when the source is not a
   player, or when the player's held item (`inventory.holdingItem`) carries the vanilla
   `weapon` tag (tools lack it). Material `explosionresistance="1"` handles explosions.
2. **Texture** - `src/dll/AdamantAtlas.cs`: postfix on `TextureAtlasBlocks.LoadTextureAtlas`
   grows the three opaque `Texture2DArray`s by one slice, fills it from
   `Resources/adamant.unity3d?adamant_diffuse` / `?adamant_normal` (512², DXT1 + DXTnm),
   appends a `uvMapping` entry and writes its index onto every `MAdamant_shapes` block.
   `blocks.xml` ships `Texture="356"` (steel) as the fallback. **No paint entry** - that
   id space is what drifts and breaks saves. Details in `docs/architecture/mechanisms.md`.
3. **Spikes-trap look** - `src/dll/AdamantTrapModel.cs`: postfixes on
   `BlockShapeModelEntity.OnBlockEntityTransformBeforeActivated` and `.CloneModel` clone the
   reused vanilla trap material once and fill three slots - `_MainTex`, **`_Normal`** (that
   is what this shader calls the bump slot) and `_RMOM` (generated uniform surface). The
   prefab and its material asset are never modified.
4. **Survival chain** - `Config/items.xml` (`adamantOre`, `adamantIngot`, tinted/iconed),
   `Config/recipes.xml` (**both** ingot and block at the workbench - the ingot must *not*
   be a `craft_area="forge"` recipe, see `docs/architecture/mechanisms.md` for why),
   `Config/blocks.xml` (rare `adamantOre` Harvest drop on `terrOreIron/Lead/Coal`).

## Common tasks

- **Change a recipe** → `AdamantBlock/Config/recipes.xml`. Verified vanilla item names:
  `resourceForgedSteel`, `resourceConcreteMix`, `resourceScrapPolymers`,
  `resourceScrapLead` (NOT `resourceLead`), `resourceWood`.
- **Add/fix a translation** → `AdamantBlock/Config/Localization.csv` **and the identical
  `Localization.txt` next to it** - V3.x reads the `.csv`, V2.x the `.txt`, and the wrong name
  fails silently (see `docs/conventions/modding.md`). 13 language columns.
  **Must be RFC-CSV**: any value containing a comma has to be quoted. Regenerate with
  `python src/texture/gen_localization.py`, which writes both files; UTF-8.
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
   zip root, forward slashes), publishes a GitHub Release with both zips, and then uploads
   both to Nexus Mods.

**Nexus automation** (Settings → Secrets and variables → Actions):

| Kind | Name | Value |
|---|---|---|
| Secret | `NEXUSMODS_API_KEY` | personal API key |
| Variable | `NEXUSMODS_FILE_ID_SURVIVAL` | Files tab → "API Info" on the mod page |
| Variable | `NEXUSMODS_FILE_ID_CREATIVE` | ditto, Creative file entry |

Each Nexus **file description** is composed in the workflow: what that edition is (Survival =
find ore/smelt/craft, Creative = 1 wood from the backpack, install one not both) + install
constraints + a mini changelog generated from the `## <version>` section of `CHANGELOG.md`.
So **write CHANGELOG bullets whose first sentence states the change** - only the opening
sentences (~180 chars, max 6 bullets) reach Nexus. Details in
[`docs/build-and-release.md`](docs/build-and-release.md).

The tag becomes the Nexus version with the leading `v` stripped. Both upload steps are
skipped while their variable is unset, so tagging works before the mod page exists —
setting/clearing the variable is the on/off switch. Only the Survival step sets
`update_mod_version`, so the mod page version tracks the main file.
- CI **cannot** build `AdamantBlock.dll` (needs the game's `Assembly-CSharp.dll`) or
  `adamant.unity3d` (needs Unity) - those binaries stay committed. **Do not remove them.**
- **Nexus upload: the FIRST one is manual, updates can be automated.** Nexus Mods has a v3
  Upload API (open beta since ~2026-03) plus an official GitHub Action,
  [`Nexus-Mods/upload-action`](https://github.com/Nexus-Mods/upload-action). Its OpenAPI
  schema has **no endpoint that creates a mod page**, so the mod page and the initial file
  entries still have to be made by hand on the website. After that,
  `POST /mod-files/{id}/versions` (what the Action wraps, keyed by a `file_id` from the
  Files tab → "API Info") can push new versions from CI. `POST /mod-files` additionally
  creates a new *file entry* on an existing mod page.
  Main file = Survival zip, optional = Creative zip. No mod requirements to list any more;
  disclose "contains DLL" and "EAC must be off".

## Gotchas (all verified on this machine)

- The MO2 path contains `[NoDelete]` - PowerShell treats `[]` as wildcards; use
  `-LiteralPath`. In Python use the Windows form `C:/Modlists/...`, not `/c/...`.
- **The log reports MO2's virtual path, not the real one.** A run launched through MO2 loads
  the mod from `%APPDATA%\7DaysToDie\Mods\AdamantBlock\`, which does not exist on disk - the
  USVFS maps the modlist folder there for the game process only. Do not conclude from that
  line that the deployment path moved; the file to overwrite is always the MO2 one above.
- **Unquoted commas in Localization.csv silently break parsing** (text truncates at the
  first comma, later columns shift).
- **PowerShell 5.1 `Compress-Archive` writes backslash zip paths** (malformed per ZIP
  spec) - build release zips with `zip` (CI) or Python `zipfile`, never Compress-Archive.
- A **running game locks `AdamantBlock.dll`** → overwrite fails with "Permission denied".
  Close 7DTD before deploying the DLL.
- **`Graphics.CopyTexture` silently refuses any pair whose mipmap limits differ.** It writes
  `different mipmap limits. Source 1, Destination 0` to the Unity log, then returns normally -
  no exception, no return value, nothing the mod can branch on. Texture Quality below Full
  puts every loaded `Texture2D` at a non-zero limit, *including ones created at runtime*,
  while `Texture2DArray` has no such property and is always at 0. So any Texture2D that is
  copied into an atlas array needs `ignoreMipmapLimit = true` - both as the importer flag
  (`ignoreMipmapLimit: 1` in the `.meta`) and defensively in code. Symptom without it: the
  block renders as flawless vanilla steel, because the slice keeps its donor pre-fill.
- **An unloaded bundle asset is fake-null, not null.** Unloading an asset bundle destroys its
  objects regardless of what still references them; the field keeps the reference but `== null`
  starts returning true. So "I already tried and it was null" is not a safe thing to cache -
  `ReferenceEquals(x, null)` distinguishes a real null (a verdict that stays true) from a
  destroyed object (a state that can be recovered by reloading). This is what made the spikes
  trap fall back to plain iron for the rest of a session after a world reload.
- Lead item is `resourceScrapLead`.

## Deeper 7DTD knowledge

Verified engine/API notes (block-damage internals, block atlas internals, tag mechanics,
icon/CSV conventions) live in the user's `7d2d-modding` Claude skill `LEARNINGS.md`
(outside this repo). Interrogate `Assembly-CSharp.dll` with that skill's `dump-*` scripts
rather than guessing engine behavior.

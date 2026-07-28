# Changelog

## 1.2.0 - 2026-07-28

### Changed

- **The OcbCustomTextures requirement is gone - Adamant now installs on its own.** The
  custom texture is injected into the opaque block atlas by `AdamantBlock.dll`: a postfix on
  `TextureAtlasBlocks.LoadTextureAtlas` grows the three `Texture2DArray`s by one slice, fills
  it from `Resources/adamant.unity3d`, appends a `uvMapping` entry and writes that index onto
  every adamant block. Nothing else to install, nothing else to keep updated.

- **No paint id is registered any more, which takes the mod out of the save-risk path.**
  The old `<opaque>` entry also created a paint-tool entry, and paint ids are stored per
  painted face in the save; dynamically assigned ones shift when the set of installed paint
  mods changes, which is what produces `Missing paint ID XML entry: N for block …` and, in
  bad cases, a crash on load. A block's own texture id comes from `blocks.xml` and is
  re-resolved at every start, so it cannot drift. The trade-off: adamant is no longer
  selectable in the paint tool.

- **`blocks.xml` now ships `Texture="356"` (vanilla steel) as a fallback.** If the texture
  cannot be installed - dedicated server without textures, missing or wrongly formatted
  bundle - the block renders as steel and stays fully playable instead of losing its texture.
  The reason is written to the log.

## 1.1.0 - 2026-07-25

### Fixed

- **Adamant Ingot could be crafted for free, one at a time.** The recipe was a
  `craft_area="forge"` recipe with ordinary item ingredients. The forge resolves and
  deducts ingredients through its molten-material grid (iron/brass/lead/glass/stone/clay
  only), so the ingredients were never found there: the craft count clamped to 1 and
  nothing was deducted, while the "do you have the items" check passed against the
  backpack. Moved to the workbench, which resolves against the player inventory. The
  crucible gate is unchanged - `resourceForgedSteel` already requires it.

- **Adamant Spikes Trap** (`adamantSpikesTrap`), both editions. 60 damage per hit
  (1.8× the vanilla iron spike) every 1.5 s, takes zero self-damage when it hurts
  something, and inherits the tool-vs-weapon gate from the existing material - immune
  to zombies, weapons and explosions, removable only with tools. Deliberately carries
  no `BlockTag="Spike"`, which is what makes it demolisher-proof. Reuses the vanilla
  iron-spike model tinted to the adamant purple, so no new art ships.
  Survival: 2 Adamant Ingots at the workbench, 1 refunded on harvest.
  Creative: 1 wood, backpack-craftable.

## 1.0.0 - 2026-07-24

Initial release for 7 Days to Die V3.0.

- Adamant block, full `shapes="All"` set, 10× steel stability, full explosion immunity.
- Tool-vs-weapon damage gate via Harmony DLL: only tools mine it; weapons, zombies,
  animals and explosions deal zero damage.
- Custom purple crystalline `adamant` texture via OcbCustomTextures (works on all
  shapes and in the paint tool).
- **Survival edition:** rare Adamant Ore (mining drop) → forge-smelted Adamant Ingot
  → workbench block. Custom 160×160 item icons for the ore and ingot.
- **Creative edition:** 1 wood → block, backpack-craftable.
- Localized into 13 languages (EN, DE, ES, FR, IT, JA, KO, PL, PT-BR, RU, TR,
  ZH-Hans, ZH-Hant).

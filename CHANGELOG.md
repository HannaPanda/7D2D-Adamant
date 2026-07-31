# Changelog

## 1.2.2 - 2026-07-31

### Fixed

- **The Adamant Spikes Trap now actually wears the adamant texture** instead of looking like
  a slightly darker iron trap. It keeps the vanilla spike shape; only the texture its model
  samples is replaced, with the same `adamant_diffuse`/`adamant_normal` the block itself uses.
  Nothing new is installed and the vanilla iron spikes trap is untouched.
- **The trap's surface now has adamant's shine and relief**, not just its colour. The first
  pass only reached the albedo: the model's shader calls its normal-map slot `_Normal`, which
  the mod did not probe, and the surface map was still the iron spike's rusty, near-matte one.
  Normal map and a uniform metallic/roughness surface matching the block are applied too now.
- The old `TintColor="7B4FB0"` on the trap never did anything and has been removed. It ends up
  as a write to a shader property this model's shader does not have, so it was silently
  ignored. Even on a shader that has it, the value is *multiplied* onto the existing texture -
  which is why vanilla only uses it on pale prefabs built for it (gun safes, chests) and never
  on a trap. The inventory icon keeps its purple tint; that is a separate code path and it
  does work.

### Documentation

- **The mod page claimed you could pick the spike trap back up. You cannot** - mining it
  returns one Adamant Ingot, half of what it cost. Corrected on the Nexus description; the
  README, which never covered the trap at all, now has a section for it.
- The README still described the Adamant Ingot as forge-smelted. It has been a workbench
  recipe since 1.1.0.

### Verified on

- **Game versions 3.0.0, 3.0.1 and 3.1.0**, each launched with the mod, checked in the log and
  looked at on screen. This release changes `AdamantBlock.dll`, so the previous list was
  discarded and re-established from scratch rather than carried over.

## 1.2.1 - 2026-07-28

### Fixed

- **Paints from other mods no longer show up wearing the adamant texture.** 1.2.0 added its
  atlas slice while the atlas was being built, which is *before* paint frameworks such as
  OcbCustomTextures register their packs (they do that while `painting.xml` loads). Growing
  the array first shifted the slices their entries pointed at, so entries in the paint tool
  rendered the wrong texture - adamant among them. The injection now runs after `painting.xml`
  and after every paint framework, where it cannot move anyone else's offsets. Only affects
  installs that also run a paint framework; nothing is stored in the save either way.

### Note

- Adamant is **not** offered in the paint tool, by design since 1.2.0 - the mod-page text
  claiming otherwise was stale. See the 1.2.0 entry for why that id space is avoided.

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

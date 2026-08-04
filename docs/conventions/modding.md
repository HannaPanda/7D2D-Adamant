# Conventions and gotchas

Verified against the installed game (7DTD V3.0) and this machine. Do not guess these;
they have each caused a concrete bug.

## Vanilla item names

Used in recipes and drops. The lead item is the trap:

| Material | Item name |
|---|---|
| Forged steel | `resourceForgedSteel` |
| Concrete mix | `resourceConcreteMix` |
| Scrap polymers | `resourceScrapPolymers` |
| Lead | `resourceScrapLead` (there is no `resourceLead`) |
| Wood | `resourceWood` |
| Coal (ore icon/mesh base) | `resourceCoal` |

## Localization.csv *and* Localization.txt

**Both files ship, with identical content.** The engine hardcodes the name it looks for and
that name changed between the game's major lines: `Localization.LoadPatchDictionaries` reads
`Config/Localization.csv` from V3.0 on and `Config/Localization.txt` on V2.x. Both sit behind
an `if (SdFile.Exists(...))`, so the wrong name produces **no warning, no error and no log
line** - the mod loads, Harmony patches, the smoke test stays green, and every block and item
shows its raw key instead of a name. That is how it went unnoticed on 2.6 until the run logs
of two versions were compared line by line and one `INF [MODS] Loading localization from mod`
was missing.

Neither version minds the other's file: `Mod.DetectContents` excludes only its own
localization name and otherwise just sets a `GameConfigMod` flag, and `XmlPatcher` looks for
vanilla config names, so it never touches either. `gen_localization.py` writes the `.txt`
twin automatically - copy both into each edition.

Real RFC-4180 CSV. Any value containing a comma must be quoted, internal quotes doubled.
An unquoted comma silently splits the field into extra columns - the text truncates at the
first comma and every later language column shifts. Generate with
`python src/texture/gen_localization.py` (uses `csv.QUOTE_MINIMAL`) rather than editing by
hand. The file is UTF-8; accents, umlauts, CJK and Cyrillic are fine.

Column order after `Context / Alternate Text`: german, spanish, french, italian, japanese,
koreana, polish, brazilian, russian, turkish, schinese, tchinese.

## Item icons

PNG in `UIAtlases/ItemIconAtlas/<name>.png`, **160×160 RGBA**, referenced by
`<property name="CustomIcon" value="<name>"/>` (value is the filename without extension).
`CustomIcon` changes the inventory icon only; the dropped-world mesh still comes from the
item's `Extends` base. High-res icon sources live in `src/texture/*_source.png`.

## Custom block texture

A `shapes="All"` block cannot carry a loose PNG - `Texture` is an index into
`TextureAtlas.uvMapping`, so a custom texture means adding a slice to the opaque
`Texture2DArray` at runtime (we do that ourselves, see architecture/mechanisms.md). Keep the
XML value **numeric**: a name only resolves if some core mod rewrites it before parsing.
Bundle asset references resolve by bare name (`?adamant_diffuse`), matching other Smorgasbord
mods. From C# the loader wants the `#<bundle path>?<asset>` form
(`DataLoader.ParseDataPathIdentifier`), with the mod folder taken from `Mod.Path`.

## Tooling and path gotchas

- The live MO2 folder is `C:\Modlists\Smorgasbord\mods\[NoDelete]Adamant Block\…`. The
  `[NoDelete]` brackets are wildcard characters in PowerShell - use `-LiteralPath`. In Python
  use the Windows form `C:/Modlists/...`, not the Git-Bash form `/c/...`.
- PowerShell 5.1 `Compress-Archive` writes zip entries with backslash separators, which is
  malformed per the ZIP spec and can break MO2/Vortex/Linux extraction. Build release zips
  with `zip` (CI does) or Python `zipfile`; never `Compress-Archive`.
- A running game locks `AdamantBlock.dll`; overwriting it fails with "Permission denied".
  Close 7DTD before deploying the DLL. XML and the texture bundle load only at startup.

## Two editions

`AdamantBlock/` (survival) and `AdamantBlock-Creative/` are alternatives - a user installs
one. They share block, material, texture, DLL and the block's localization. They differ in
`recipes.xml`, and survival additionally has `items.xml`, the ore drops in `blocks.xml`, the
ore/ingot icons, and the extra localization rows. When changing a shared file, update both.

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

## Localization.csv

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

A `shapes="All"` block cannot carry a loose PNG - `Texture` is an atlas index, and a custom
one needs OcbCustomTextures (see architecture/mechanisms.md). Bundle asset references resolve
by bare name (`?adamant_diffuse`), matching other Smorgasbord mods.

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

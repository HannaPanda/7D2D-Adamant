# Nexus mod page assets

`description.bbcode` is the source of truth for the Nexus Mods description. Edit it here,
then paste it into the mod page's description field (the Nexus editor has a **BBCode** toggle -
paste into that, not the rich-text view, or the tags get escaped).

## Image placeholders

The file contains four `%%IMG_*%%` placeholders. Nexus cannot host an image from a
description alone - it has to exist in the mod page's **Images** tab first:

1. Mod page → **Images** → upload the screenshot.
2. Open the uploaded image, copy its **direct image URL**
   (`https://staticdelivery.nexusmods.com/mods/.../images/....jpg`).
3. Replace the placeholder with that URL.

| Placeholder | What to shoot |
|---|---|
| `%%IMG_HERO%%` | The money shot. A finished adamant structure, good light, ideally at dusk so the purple reads. This is the thumbnail people judge the mod by. |
| `%%IMG_SHAPES%%` | Several different shapes side by side (cube, ramp, wedge, plate, pillar) to prove `shapes="All"` works. Not the paint tool - adamant is deliberately not a paint (see CHANGELOG 1.2.0). |
| `%%IMG_TRAPS%%` | A spike row in front of a base during a blood moon, zombies in it. Action sells traps far better than an empty row. |
| `%%IMG_CRAFTING%%` | The workbench recipe open, or the three items laid out in inventory (ore → ingot → block). Proves the progression at a glance. |

Fewer, better images beat more images. If you only shoot two, make them the hero and the traps.

## Notes

- Nexus renders on a **dark** background - the accent colour `#b78ae8` is a lightened
  adamant purple picked to stay readable there. The block's own texture purple (`#7B4FB0`,
  from `src/texture/gen_adamant_512.py`) is too dark for text on that background.
- The `━` separator lines are plain Unicode box-drawing characters, not a BBCode tag, so they
  render everywhere. Nexus has no reliable horizontal-rule tag.
- Tags used: `size, b, i, color, center, url, img, list, list=1, quote`. All standard, but
  **use the Nexus preview before saving** - the exact tag support is not publicly documented.
- Keep this file in sync when recipes or stats change. It duplicates numbers from
  `AdamantBlock/Config/` on purpose, since the mod page cannot read the XML.
- The **Changelog** section is hand-maintained: add a `[size=3][b]X.Y.Z[/b][/size]` block per
  release, newest on top, and keep it to what a player notices - the reason for a change, not
  its implementation. It is deliberately not the same text as `CHANGELOG.md` (which is written
  for the repo) nor as the per-file mini changelog CI generates for the Files tab. Old versions
  can be trimmed once they are several releases back; the link to the full history covers them.

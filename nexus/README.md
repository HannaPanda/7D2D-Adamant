# Nexus mod page assets

`description.bbcode` is the source of truth for the Nexus Mods description. Edit it here,
then paste it into the mod page's description field. The rich-text editor converts the
BBCode correctly on paste, so no mode switch is needed.

## Images

The description **hotlinks the screenshots straight out of this repo**, so a new one is a
commit and not a round trip through the Nexus Images tab:

```
https://raw.githubusercontent.com/HannaPanda/7D2D-Adamant/refs/heads/main/nexus/images/<file>
```

| File | What it shows |
|---|---|
| `AdamantHero.jpg` | Title card over the finished base at dusk, with the four-icon feature strip. The thumbnail people judge the mod by. |
| `AdamantFortress.jpg` | The same base plain: stilts and centre column in adamant, ladder above zombie reach. Shows the material doing its actual job and, incidentally, several shapes at once. |
| `AdamantGround.jpg` | Looking down from the outrigger at a square of adamant set into the concrete floor. Backs the "shoot your own floor" section - the one use of the tool-vs-weapon gate a reader will not think of on their own. |
| `AdamantTrap.jpg` | Close-up of the spike trap, purple crystalline and glossy. Also the visual proof that 1.2.2 fixed its look. |

Both the Fortress and Ground shots carry an italic caption line underneath, because neither
reads as a *feature* without one - a purple pillar is just a purple pillar until the text says
the horde cannot chew it.

**To swap an image:** drop the new file in `nexus/images/` under the same name, commit, push.
The description needs no edit and the mod page updates on its next load - GitHub sends a short
`max-age` on raw content, so it is a refresh, not a cache eviction.

Rules for what goes in this folder:

- **JPEG, max 1600 px wide, `-q:v 3`.** The originals are 1080p PNGs at 2.1-4.5 MB each;
  hotlinked as-is the description would pull ~12.8 MB. Converted it is ~800 KB for all four:
  ```
  ffmpeg -y -i in.png -vf "scale='min(1600,iw)':-2" -q:v 3 out.jpg
  ```
- **Keep the filenames stable.** They are baked into the description; renaming one silently
  breaks a live mod page.
- The uncompressed originals are not kept here - this folder holds the web copies only.

**Two things this does not replace:**

1. **Still upload the screenshots to the mod page's Images tab.** The gallery, the thumbnail
   and the search preview all come from there, not from the description. The hotlinking only
   saves the copy-the-CDN-URL step for images embedded *in the body*.
2. **Nexus renders `raw.githubusercontent.com` images fine** - confirmed on the 7 Dashes to
   Die page, which uses the same setup. No fallback needed. (Should that ever change, upload
   the files and paste the resulting `https://staticdelivery.nexusmods.com/mods/.../images/....jpg`
   URLs instead; the layout is unchanged, only the four URLs differ.)

**Push before pasting the description.** The URLs resolve only once `nexus/images/` is on
GitHub - a description pasted from an unpushed working copy renders four broken images.

Still missing, in rough order of value: **a blood-moon shot with zombies actually standing in
the spike trap** (action sells traps; an empty row does not), and a crafting shot (ore → ingot
→ block, or the open workbench recipe).

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
  release, newest on top. **Write it for players, not for modders** - no atlas/slice/paint-id/
  Harmony vocabulary, no class or file names. Say what someone notices and what to do about it,
  keep the reason only where it affects a decision (a removed feature, a save-related risk), and
  say plainly when saves are safe. It is deliberately not the same text as `CHANGELOG.md` (repo,
  technical) nor the per-file mini changelog CI generates for the Files tab. Old versions can be
  trimmed once they are several releases back; the link to the full history covers them.

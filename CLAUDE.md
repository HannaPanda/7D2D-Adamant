# CLAUDE.md

Project context and workflows for this repo live in **[AGENTS.md](AGENTS.md)** - read it first.

@AGENTS.md

## Quick reminders for Claude Code

- This is a **7 Days to Die V3.0 / V3.1** mod. The repo mirrors a live MO2 deployment at
  `C:\Modlists\Smorgasbord\mods\[NoDelete]Adamant Block\AdamantBlock\` - keep them in sync.
- Prefer the **`7d2d-modding` skill** for any engine/API question; it interrogates the real
  `Assembly-CSharp.dll` instead of guessing, and its `LEARNINGS.md` records the traps.
- Before deploying the DLL, make sure 7DTD is **not running** (it locks the file).
- Validate `Config/*.xml` and the RFC-CSV `Localization.csv`/`.txt` pair after edits (both
  ship: V3.x reads the csv, V2.x the txt, silently); releases go out
  via a version tag (`git tag vX.Y.Z && git push origin vX.Y.Z`), which triggers CI.

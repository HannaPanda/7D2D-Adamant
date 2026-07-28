# Build and release

## DLL

`src/dll/AdamantBlock.csproj` references the game's `Assembly-CSharp.dll`, `0Harmony.dll`
and `UnityEngine*.dll` by absolute path (`C:\Steam\steamapps\common\7 Days To Die\…`), so it
only builds on a machine with that install.

```
cd src/dll
DOTNET_ROLL_FORWARD=LatestMajor dotnet build -c Release -o out
```

Copy `out/AdamantBlock.dll` into both `AdamantBlock/` and `AdamantBlock-Creative/`. Verify the
header is a real assembly (`MZ` / `4d5a`); a truncated build shows an all-zero header.

## Texture bundle

`Resources/adamant.unity3d` is built in Unity **2022.3.62f2** (Built-in pipeline, Linear).
It must contain two texture assets named `adamant_diffuse` (DXT1, no alpha, 512²) and
`adamant_normal` (Normal map / DXT5, 512²), both assigned to AssetBundle `adamant` variant
`unity3d`. Source PNGs and the generator are in `src/texture/`. This step cannot run in CI.

## Regenerating source art

- Block textures: `python src/texture/gen_adamant_512.py` (numpy + Pillow) → 512² diffuse
  and normal.
- Item icons: the shipped icons are ChatGPT-rendered; hi-res originals are
  `src/texture/*_source.png`. `gen_icons.py` produces the earlier procedural fallbacks.
- Localization: `python src/texture/gen_localization.py`.

## Release

CI (`.github/workflows/release.yml`) packages and publishes on a version tag:

1. Bump `<Version>` in both `ModInfo.xml`, update `CHANGELOG.md`.
2. `git tag vX.Y.Z && git push origin vX.Y.Z`.
3. The workflow validates every XML, checks the installable structure, zips both editions
   (mod folder at zip root, forward slashes) and publishes a GitHub Release with the zips.

CI cannot build the DLL or the `.unity3d` bundle, so those two binaries are committed. Do not
remove them or the release zips would be incomplete.

## Nexus upload

The mod page and the two file entries are created **by hand once** (the v3 Upload API has no
endpoint that creates a mod page). After that CI pushes every tag: Survival as the main file,
Creative as optional. See AGENTS.md for the secret/variable names.

### File descriptions

The description on a Nexus *file* is what a user reads next to the download button, so it
answers "which of these two do I want?" first and "what changed?" second. Never ship
boilerplate like "Automated upload from tag vX.Y.Z". The workflow builds each description as:

1. **What this edition is**, in the user's terms - Survival: find ore, smelt an ingot, craft
   at the workbench; Creative: 1 wood from the backpack, for builders and testing. Both say
   "install this OR the other one, not both".
2. **Install constraints** - OcbCustomTextures required, EAC off (Harmony DLL), multiplayer
   needs client + server.
3. **Mini changelog** - built by the `Build mini changelog` step from the `## <version>`
   section of `CHANGELOG.md`: up to 6 top-level bullets, each flattened to one line
   (~180 chars, whole sentences where they fit), followed by a link to the full changelog.
   No matching section → the blurb plus the link, so a forgotten changelog entry never
   breaks the release.

The blurbs live in `release.yml` next to each upload step; edit them there when an edition's
premise changes. Because only the bullet's opening sentences survive, **write CHANGELOG
bullets so the first sentence states the change** - the rest of the bullet can hold the
reasoning for readers of the repo.

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

Manual — there is no upload API or CLI. Upload the survival zip as the main file and the
creative zip as an optional file. List OcbCustomTextures (Nexus mod 2788) as a requirement,
disclose that the mod contains a DLL, and note that EasyAntiCheat must be off and that
multiplayer needs it on client and server.

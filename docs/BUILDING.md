# Building

Requires the .NET SDK, .NET Framework 4.8 reference assemblies, Windows PowerShell, and a local Valheim installation with BepInEx.

## Debug

```powershell
dotnet build ReferToCompendium.sln -c Debug -p:DeployToGame=true
```

After a successful build, the final DLL is copied to `BepInEx/plugins` and its SHA-256 is checked. Use `-p:DeployToGame=false` to skip the copy. Debug never creates a distribution ZIP, even if `BuildPackages=true` is supplied.

## Release

```powershell
dotnet build ReferToCompendium.sln -c Release
```

This creates `Thunderstore/ReferToCompendium_v<major.minor.patch>.zip`. The version comes from the final DLL's assembly version. The ZIP contains exactly these files at its root:

```text
ReferToCompendium.dll
ReferToCompendium.English.yml
README.md
CHANGELOG.md
LICENSE
ThirdPartyNotices.txt
manifest.json
icon.png
```

The package uses the root README and the metadata, changelog, and 256x256 PNG icon in `Thunderstore/`. The English YAML is copied directly from `translations/English.yml`. The packaged manifest receives the DLL version without rewriting the source manifest. Update the project version, `Plugin.PluginVersion`, source manifest version, and changelog together when preparing a release.

The archive is written to a temporary file and its entries are hash-checked before it becomes a `.zip`. YamlDotNet is merged into the final plugin DLL before packaging. No separate library DLLs, game/reference DLLs, PDBs, user configs, or personal layout files are bundled.

Use `-p:BuildPackages=false` for a Release DLL without packaging. Use `-p:PackageOutputDirectory=C:\path\outside\watched-folders` for packaging-only work that must not trigger an existing ZIP watcher. Release builds do not copy the DLL into the game. No upload commands, release markers, or uploader setup are included in the build.

## Validate inputs without creating a ZIP

After a Debug build:

```powershell
dotnet msbuild ReferToCompendium.csproj -t:ValidateThunderstorePackage -p:Configuration=Debug
```

This checks the DLL identity, manifest, required files, and icon dimensions. It does not create an archive or alter the source manifest.

## Paths

The default game directory is `C:\Program Files (x86)\Steam\steamapps\common\Valheim`. Override `GamePath`, `GameManagedPath`, `BepInExPath`, `GamePluginDirectory`, or `PackageOutputDirectory` with MSBuild properties or a local `environment.props`. Compile against original game assemblies; no publicizer is used.

## Checks

```powershell
dotnet run --project tests/LayoutStateChecks -c Debug
```

`tests/OriginalApiChecks` checks the built DLL against original game metadata. `tests/RuntimeChecks/Verify-GameCompatibility.ps1` uses the configured original game Mono snapshot for isolated managed checks. These checks do not launch Unity or verify the editor's appearance and third-party HUD rendering in-game.

## Status HUD behavior and verification

The status HUD has five client config entries: **Single column**, **Position**, **Scale**, **Row spacing**, and **Hidden effects**. It does not add status ordering, a separate editor, or another saved-layout file. The Compendium layout remains independent.

Filtering uses the incoming HUD list and preserves the relative order of retained effects. Never sort or remove entries from the player's `SEMan` list, change the effect objects, or rebuild the HUD list from `ObjectDB`: that would interfere with gameplay or restore entries hidden by another mod.

Hidden keys are separated by commas, semicolons, CR, or LF. Whitespace around each key is ignored. Matching uses exact, case-sensitive internal `Object.name` values or raw `StatusEffect.m_name` values, without localization. A terminal `(Clone)` suffix is ignored on runtime effect names; use the unsuffixed name in the config. There are no wildcards, partial-name matches, or `=On`/`=Off` suffixes. `Rested` and `Resting` are game-defined internal names; modded keys should come from that mod's documentation or source.

Before release, verify the following in an actual game scene:

- Enable the column and change position, scale, and row spacing. Check icons, names, timers, and effects being added or expiring. Disable it and confirm the original arrangement and styling return.
- Test hidden keys with the column both on and off. Clear the list and confirm entries return. Check that gameplay effects and the Compendium's **Active effects** page are unaffected.
- Check inventory open/close, map and no-map worlds, UI scaling, and a new session. The column uses the same top-right coordinates in map and no-map worlds.
- Test filtering alongside StatusEffectFilter. For layout testing, disable CompactStatusEffects and MyLittleUI's status list and custom element overrides as described in the README; restart after disabling CompactStatusEffects. Do not rewrite their configs automatically.

Managed filter checks and original API checks do not verify Unity layout restoration, input handling, or third-party HUD behavior. Report that runtime scope separately.

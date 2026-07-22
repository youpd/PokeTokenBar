# PokeTokenBar for Windows

PokeTokenBar turns local Claude Code, Codex, and Gemini CLI usage into a tray companion. The Windows
port provides the same usage tracking, official limits, alerts, companion game, shop, bag, collection,
and Korean/English/Japanese UI as the macOS app.

## Install

The packaged build supports 64-bit Windows 10 version 1809 or newer. It is self-contained, so a
separate .NET installation is not required.

1. Download `PokeTokenBar-win-x64.zip` from a `win-vX.Y.Z` GitHub release.
2. Extract the archive to a permanent directory.
3. Run `PokeTokenBar.exe`. The app appears in the notification area instead of the taskbar.
4. Open Settings from the tray menu to enable launch at login or change language and display options.

The first unsigned release may show a Microsoft Defender SmartScreen warning. Review the publisher
and release checksum before choosing **More info → Run anyway**. The SHA-256 checksum is distributed
beside every release archive.

### Scoop

After the corresponding GitHub release is published, install the checked-in manifest directly:

```powershell
scoop install https://raw.githubusercontent.com/youpd/PokeTokenBar/main/windows/scoop/poke-token-bar.json
```

## What the app reads

Usage stays on this computer. The app reads local logs from `%USERPROFILE%\.claude`, the Codex home,
and `%USERPROFILE%\.gemini`; extra home directories can be added in Settings. Claude's optional
official-limit view reads the existing Claude credential file, while Codex limits use
`codex app-server` without starting a model turn. Pokémon metadata and sprites are fetched from
PokéAPI at runtime and cached locally; no Pokémon assets are bundled.

Application state is stored under `%LOCALAPPDATA%\PokeTokenBar`:

- `settings.json`, `companion-state.json`, and incremental usage caches
- `last-snapshot.json` for local parity diagnostics
- `sprites\` for runtime-fetched images
- `logs\app.log` for troubleshooting

Use **Settings → Report a problem** to create a diagnostic email, or **Show logs** to open the log
directory.

## Run from source

Install the .NET 10 SDK, then run these commands from the repository root:

```powershell
dotnet build windows/PokeTokenBar.sln
dotnet test windows/PokeTokenBar.sln
dotnet run --project windows/src/PokeTokenBar.App/PokeTokenBar.App.csproj
```

Create the same self-contained release archive used by CI:

```powershell
./windows/scripts/package.ps1 -Version 0.1.0
```

The package and checksum are written to `windows/artifacts/`. Pushing a matching `win-v0.1.0` tag
runs tests, packages the app, and creates the Windows GitHub release. macOS releases continue to use
the separate `vX.Y.Z` tag namespace.

## License and disclaimer

The original source code is MIT licensed; see the root `LICENSE` and
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md). PokeTokenBar is an unofficial, non-commercial fan
project and is not affiliated with Nintendo, Game Freak, Creatures Inc., or The Pokémon Company.
No Pokémon assets are included in this repository or its release archives.

# WoshiRepo

Dalamud plugins for FINAL FANTASY XIV.

| Plugin | Does |
|---|---|
| [Retainer Undercut](RetainerUndercut) | Reprices every listing on the open retainer (`/uc`) |
| [Sub Salvage](SubSalvage) | Vendor-sells submarine loot, keeping 1 of each item (`/salvage`) |

## Install

In game, open `/xlsettings` → Experimental, add this under Custom Plugin Repositories, and save:

```
https://raw.githubusercontent.com/WoshiDev/WoshiRepo/main/repo.json
```

The plugins then show up in `/xlplugins`.

## Development

Each plugin is its own folder with a `.csproj` on `Dalamud.NET.Sdk`. Needs the .NET SDK and XIVLauncher with Dalamud installed.

```
dotnet build RetainerUndercut -c Release
```

To test without publishing, add `<Plugin>\bin\Release\<Plugin>.dll` under `/xlsettings` → Experimental → Dev Plugin Locations.

### Adding a plugin

Copy an existing plugin folder as a starting point, rename the folder, `.csproj`, `.json` manifest and namespace, then add a row to the table above.

### Publishing

Bump `<Version>` in the plugin's `.csproj`, then:

```
.\publish.ps1 RetainerUndercut
```

It builds the plugin, uploads `latest.zip` as a GitHub release tagged `<Plugin>-<version>`, and updates and pushes `repo.json`. Needs the GitHub CLI (`gh`) signed in.

## Disclaimer

Third-party plugins are against the FINAL FANTASY XIV terms of service. Use at your own risk.

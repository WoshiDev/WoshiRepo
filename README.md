# Retainer Undercut

A Dalamud plugin for FINAL FANTASY XIV that reprices every listing on the open retainer.

For each listing it opens **Adjust Price → Compare Prices**, takes the undercut price that another plugin copies to the clipboard, and confirms the new price. It skips items where you already have the cheapest listing (HQ and NQ are compared separately), and reuses the price for repeat listings of the same item in one run.

## Usage

Open a retainer's sell list at a summoning bell, then:

| Command | Does |
|---|---|
| `/undercut` | Reprice every listing |
| `/undercut dry` | Test run: goes through every item, changes nothing |
| `/undercut stop` | Cancel |
| `/undercut window` | Main window (Run / Dry run / Stop, results) |
| `/undercut config` | Settings |

Requires a separate plugin that copies the undercut price to the clipboard when the price comparison window opens.

## Building

Needs the .NET SDK and XIVLauncher with Dalamud installed.

```
dotnet build RetainerUndercut -c Release
```

Then add `RetainerUndercut\bin\Release\RetainerUndercut.dll` under `/xlsettings` → Experimental → Dev Plugin Locations.

## Disclaimer

Third-party plugins are against the FINAL FANTASY XIV terms of service. Use at your own risk.

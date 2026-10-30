# Retainer Undercut

A Dalamud plugin for FINAL FANTASY XIV that reprices every listing on the open retainer.

For each listing it opens **Adjust Price → Compare Prices**, reads the market board listings, and sets your price to the cheapest other seller of the same quality minus a set amount (1 gil by default), moving it up or down as needed. Your own listings are ignored, so it never undercuts you. It reuses the price for repeat listings of the same item in one run.

It can also put new stacks of food and potions from your bags up for sale: pick the item in the main window, choose how many stacks of 99, and it lists each one 1 gil under the cheapest other seller.

## Usage

Open a retainer's sell list at a summoning bell, then:

| Command | Does |
|---|---|
| `/uc` | Reprice every listing |
| `/uc dry` | Test run: goes through every item, changes nothing |
| `/uc stop` | Cancel |
| `/uc window` | Main window (Run / Dry run / Stop, results) |
| `/uc config` | Settings |

## Building

Needs the .NET SDK and XIVLauncher with Dalamud installed.

```
dotnet build RetainerUndercut -c Release
```

Then add `RetainerUndercut\bin\Release\RetainerUndercut.dll` under `/xlsettings` → Experimental → Dev Plugin Locations.

## Disclaimer

Third-party plugins are against the FINAL FANTASY XIV terms of service. Use at your own risk.

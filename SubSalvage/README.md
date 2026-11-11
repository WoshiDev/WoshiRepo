# Sub Salvage

A Dalamud plugin for FINAL FANTASY XIV that vendor-sells submarine loot and leaves 1 of each item in your bags.

It always sells the Salvaged and Extravagant Salvaged accessories (Ring, Bracelet, Earring, Necklace). With **Include default MOJ/OJ drops** ticked (the default), it also sells Gold Ingot, Platinum Ingot, Ruby, Star Ruby, Diamond, Emerald, Sapphire and Star Sapphire. Add any other trash from the main window by picking it from what's in your bags. Your items sell first, then the MOJ/OJ drops, then the Salvaged accessories.

Only the main inventory is touched. NQ and HQ are kept apart, so 1 of each quality stays in your bags. To keep 1 of a stack, it splits all but 1 into an empty slot and sells that, so the 1 kept stays where it was and your bag order doesn't change. A split needs one free bag slot. If you have more than one stack of an item, the smaller stacks are sold whole first, which can free one up, and the largest stack is the one split down to 1. If your bags are still full when a split is needed, it stops and asks you to make room. It never sells your last one.

## Usage

Open a merchant's shop, or a retainer's inventory or sell list at a summoning bell, then:

| Command | Does |
|---|---|
| `/salvage` | Sell |
| `/salvage dry` | List what would be sold, changes nothing (works anywhere) |
| `/salvage stop` | Cancel |
| `/salvage window` | Main window (item list, Sell / Dry run / Stop, results) |

## Building

```
dotnet build SubSalvage -c Release
```

Then add `SubSalvage\bin\Release\SubSalvage.dll` under `/xlsettings` → Experimental → Dev Plugin Locations.

Self-check for the sell planning: `dotnet run --project SubSalvage/Check`

## Disclaimer

Third-party plugins are against the FINAL FANTASY XIV terms of service. Use at your own risk.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace RetainerUndercut;

// Putting new stacks of food and potions up for sale on the open retainer.
public sealed partial class Plugin
{
    public const int ListingStackSize = 99;
    public const int MaxRetainerListings = 20;

    // ItemUICategory rows, checked against the game data.
    private static readonly HashSet<uint> SellableCategories = [44 /* Medicine */, 46 /* Meal */];
    private static readonly InventoryType[] Bags =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

    public record SellableItem(uint ItemId, bool Hq, string Name, int Quantity, int FullStacks);

    // Food and potions in your bags, one entry per item and quality.
    public static unsafe List<SellableItem> SellableItems()
    {
        var sheet = Data.GetExcelSheet<Item>();
        var found = new Dictionary<(uint, bool), (int Quantity, int Stacks)>();
        foreach (var bag in Bags)
        {
            var container = InventoryManager.Instance()->GetInventoryContainer(bag);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item->IsEmpty()) continue;
                var id = item->GetBaseItemId();
                if (!sheet.TryGetRow(id, out var row) || !SellableCategories.Contains(row.ItemUICategory.RowId) || row.IsUntradable) continue;

                var key = (id, item->IsHighQuality());
                found.TryGetValue(key, out var sum);
                found[key] = (sum.Quantity + item->Quantity, sum.Stacks + item->Quantity / ListingStackSize);
            }
        }
        return found
            .Select(e => new SellableItem(e.Key.Item1, e.Key.Item2, sheet.GetRow(e.Key.Item1).Name.ExtractText(), e.Value.Quantity, e.Value.Stacks))
            .OrderBy(e => e.Name).ThenBy(e => e.Hq)
            .ToList();
    }

    public static bool SellListOpen() => IsReady("RetainerSellList");
    public static int FreeListingSlots() => MaxRetainerListings - ListingCount();

    public void StartListing(SellableItem item, int stacks, bool dryRun) =>
        StartJob(token => ListStacks(item, stacks, dryRun, token));

    private static async Task ListStacks(SellableItem item, int stacks, bool dryRun, CancellationToken token)
    {
        var label = item.Hq ? $"{item.Name} (HQ)" : item.Name;
        int? price = null;

        for (var n = 1; n <= stacks; n++)
        {
            await WaitFor("RetainerSellList", token);
            var before = await Framework.RunOnFrameworkThread(ListingCount);
            if (before >= MaxRetainerListings) { Log("This retainer has no free market slots left."); break; }

            if (!await Framework.RunOnFrameworkThread(() => OpenSellMenu(item.ItemId, item.Hq)))
            {
                Log($"No more full stacks of {label} in your bags.");
                break;
            }
            await WaitFor("ContextMenu", token);
            await Framework.RunOnFrameworkThread(SelectPutUpForSale);

            try { await WaitFor("RetainerSell", token); }
            catch (TimeoutException)
            {
                var open = await Framework.RunOnFrameworkThread(OpenWindows);
                throw new TimeoutException($"The sell window didn't open after picking \"Put Up for Sale\". Open windows: {open}");
            }
            var compared = price == null;
            if (compared)
            {
                await Framework.RunOnFrameworkThread(() => Fire("RetainerSell", SellComparePrices));
                string? problem;
                if (await WaitForListings(item.ItemId, token) is { } state)
                    problem = $"market data didn't load: {state}";
                else
                    (price, problem) = await Framework.RunOnFrameworkThread(() => NewListingPrice(item.ItemId, item.Hq));
                await Framework.RunOnFrameworkThread(() =>
                {
                    Close("ItemSearchResult");
                    Close("ItemHistory");
                });
                await Task.Delay(StepDelay, token);

                if (problem != null)
                {
                    Log($"{label}: not listed ({problem}).");
                    await Framework.RunOnFrameworkThread(() => Fire("RetainerSell", SellCancel));
                    break;
                }
            }

            var p = price!.Value;
            if (dryRun)
            {
                Log($"(dry run) Stack {n}: would list {ListingStackSize} {label} at {p:N0} each.");
                await Framework.RunOnFrameworkThread(() => Fire("RetainerSell", SellCancel));
            }
            else
            {
                await Framework.RunOnFrameworkThread(() => SetQuantityPriceAndConfirm(ListingStackSize, p));
                Log($"Stack {n}: listed {ListingStackSize} {label} at {p:N0} each.");
                // Wait for the listing to land so the next stack doesn't pick the same items again.
                for (var waited = 0; waited < 5000 && await Framework.RunOnFrameworkThread(ListingCount) == before; waited += 100)
                    await Task.Delay(100, token);
            }

            await Task.Delay(compared ? ItemDelay : StepDelay, token);
        }
        Log("Done.");
    }

    // 1 gil (the undercut amount) under the cheapest other seller of the same quality.
    // If you're the only seller, matches your own cheapest listing.
    private static (int? Price, string? Problem) NewListingPrice(uint itemId, bool hq)
    {
        var (mine, others) = CheapestPrices(itemId, hq);
        if (others == uint.MaxValue && mine == uint.MaxValue) return (null, "nobody is selling it, so there's no price to undercut");
        var price = others == uint.MaxValue ? (int)mine : Math.Max(1, (int)others - Config.UndercutAmount);
        return (price, null);
    }

    // Opens the item menu on a bag slot holding at least a full stack. False if there isn't one.
    private static unsafe bool OpenSellMenu(uint itemId, bool hq)
    {
        foreach (var bag in Bags)
        {
            var container = InventoryManager.Instance()->GetInventoryContainer(bag);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item->IsEmpty() || item->GetBaseItemId() != itemId || item->IsHighQuality() != hq ||
                    item->Quantity < ListingStackSize) continue;

                var inventoryAgent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory);
                AgentInventoryContext.Instance()->OpenForItemSlot(bag, i, 0, inventoryAgent->GetAddonId());
                return true;
            }
        }
        return false;
    }

    // Picks "Put Up for Sale" in the item menu, matched by the game's own text so any client language works.
    // The callback takes the entry's position in the menu, counted from the first entry text
    // (value 8 when checked in game, after a header of counts and empty values).
    private static unsafe void SelectPutUpForSale()
    {
        var sheet = Data.GetExcelSheet<Addon>();
        var texts = new[] { 99u, 956u }.Select(id => sheet.GetRow(id).Text.ExtractText()).ToArray();

        var menu = Addon("ContextMenu");
        var entries = new List<string>();
        for (var i = 0; i < menu->AtkValuesCount; i++)
        {
            var value = menu->AtkValues[i];
            if ((value.Type & AtkValueType.TypeMask) is not (AtkValueType.String or AtkValueType.ConstString))
            {
                if (entries.Count > 0) break; // the entry texts are one unbroken run
                continue;
            }
            var text = value.String.ToString();
            if (texts.Any(text.Contains))
            {
                Fire("ContextMenu", 0, entries.Count, 0);
                return;
            }
            entries.Add(text);
        }
        menu->Close(true);
        throw new InvalidOperationException($"No \"{texts[0]}\" in the item menu. It had: {string.Join(", ", entries)}");
    }

    // Names of the game windows currently showing (HUD elements left out).
    private static unsafe string OpenWindows()
    {
        var names = new List<string>();
        var list = RaptureAtkUnitManager.Instance()->AllLoadedUnitsList;
        foreach (var unit in list.Entries[..list.Count])
        {
            var addon = unit.Value;
            if (addon != null && addon->IsVisible && !addon->NameString.StartsWith('_')) names.Add(addon->NameString);
        }
        return string.Join(", ", names);
    }

    private static unsafe void SetQuantityPriceAndConfirm(int quantity, int price)
    {
        SellAddon()->Quantity->SetValue(quantity);
        SetPriceAndConfirm(price);
    }
}

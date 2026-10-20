using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RetainerUndercut;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IChatGui Chat { get; set; } = null!;
    [PluginService] private static IPlayerState Player { get; set; } = null!;

    public static Configuration Config { get; private set; } = null!;
    // Lines from the latest run, shown in the main window. Lock it before reading or writing.
    public static readonly List<string> Results = [];

    private static TimeSpan StepDelay => TimeSpan.FromMilliseconds(Config.StepDelayMs);
    // Gap between items so the market board doesn't throttle the price comparison requests.
    private static TimeSpan ItemDelay => TimeSpan.FromMilliseconds(Config.ItemDelayMs);

    // Addon callback values. If a step does nothing in game, these are the first thing to check.
    private static readonly int[] SellListOpenMenu = [0, -1, 1]; // -1 is replaced with the slot index
    private static readonly int[] MenuAdjustPrice = [0, 0, 0];
    private const int SellConfirm = 0, SellCancel = 1, SellComparePrices = 4;

    private readonly WindowSystem windows = new("RetainerUndercut");
    private readonly MainWindow mainWindow;
    public ConfigWindow ConfigWindow { get; } = new();
    private CancellationTokenSource? cts;

    public bool IsRunning => cts != null;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        mainWindow = new MainWindow(this) { Size = new(420, 300), SizeCondition = ImGuiCond.FirstUseEver };
        windows.AddWindow(mainWindow);
        windows.AddWindow(ConfigWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += ConfigWindow.Toggle;

        Commands.AddHandler("/undercut", new CommandInfo(OnCommand)
        {
            HelpMessage = "Reprice all listings on the open retainer.\n" +
                          "/undercut dry - test run, changes nothing\n" +
                          "/undercut stop - cancel\n" +
                          "/undercut window - open the main window\n" +
                          "/undercut config - open settings",
        });
    }

    public void Dispose()
    {
        cts?.Cancel();
        Commands.RemoveHandler("/undercut");
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= ConfigWindow.Toggle;
        windows.RemoveAllWindows();
    }

    public static void SaveConfig() => PluginInterface.SavePluginConfig(Config);

    private void OnCommand(string command, string args)
    {
        switch (args.Trim())
        {
            case "stop": Stop(); break;
            case "window": mainWindow.Toggle(); break;
            case "config": ConfigWindow.Toggle(); break;
            case "dry": Start(dryRun: true); break;
            default: Start(dryRun: false); break;
        }
    }

    public void Stop() => cts?.Cancel();

    public void Start(bool dryRun)
    {
        if (IsRunning) { Chat.Print("[Undercut] Already running."); return; }

        lock (Results) Results.Clear();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Task.Run(async () =>
        {
            try { await Run(dryRun, token); }
            catch (OperationCanceledException) { Log("Stopped."); }
            catch (Exception e)
            {
                lock (Results) Results.Add($"Error: {e.Message}");
                Chat.PrintError($"[Undercut] {e.Message}");
            }
            finally { cts = null; }
        });
    }

    private static void Log(string message)
    {
        lock (Results) Results.Add(message);
        if (Config.PrintToChat) Chat.Print($"[Undercut] {message}");
    }

    private static async Task Run(bool dryRun, CancellationToken token)
    {
        var count = await Framework.RunOnFrameworkThread(ListingCount);
        if (count == 0) throw new InvalidOperationException("No listings found. Open a retainer's sell list first.");

        // Price already set this run for each item and quality, so repeat listings skip the price comparison.
        var known = new Dictionary<(uint ItemId, bool Hq), int>();

        for (var i = 0; i < count; i++)
        {
            await WaitFor("RetainerSellList", token);
            var slot = i;
            var key = await Framework.RunOnFrameworkThread(() => ItemKey(slot));
            await Framework.RunOnFrameworkThread(() =>
                Fire("RetainerSellList", SellListOpenMenu.Select(v => v == -1 ? slot : v).ToArray()));

            await WaitFor("ContextMenu", token);
            await Framework.RunOnFrameworkThread(() => Fire("ContextMenu", MenuAdjustPrice));

            await WaitFor("RetainerSell", token);
            var current = await Framework.RunOnFrameworkThread(AskingPrice);

            int? newPrice;
            string? skip;
            var reused = key is { } k && known.ContainsKey(k);
            if (reused)
            {
                newPrice = known[key!.Value];
                skip = newPrice >= current ? $"same item as earlier, {newPrice:N0} isn't lower" : null;
            }
            else
            {
                await Framework.RunOnFrameworkThread(() =>
                {
                    ImGui.SetClipboardText("");
                    Fire("RetainerSell", SellComparePrices);
                });
                newPrice = await WaitForClipboardPrice(token);
                skip = await Framework.RunOnFrameworkThread(() => SkipReason(slot, current, newPrice));
                await Framework.RunOnFrameworkThread(() =>
                {
                    Close("ItemSearchResult");
                    Close("ItemHistory");
                });
                if (skip == null && key != null) known[key.Value] = newPrice!.Value;
            }
            await Task.Delay(StepDelay, token);

            var note = reused ? " (same item as earlier)" : "";
            if (dryRun)
            {
                Log($"(dry run) Slot {i + 1}: current {current:N0}, " +
                    (skip ?? $"would set {newPrice:N0}{note}") + ".");
                await Framework.RunOnFrameworkThread(() => Fire("RetainerSell", SellCancel));
            }
            else if (skip != null || newPrice is not { } price)
            {
                Log($"Slot {i + 1}: kept {current:N0} ({skip}).");
                await Framework.RunOnFrameworkThread(() => Fire("RetainerSell", SellCancel));
            }
            else
            {
                await Framework.RunOnFrameworkThread(() => SetPriceAndConfirm(price));
                Log($"Slot {i + 1}: {current:N0} -> {price:N0}{note}");
            }

            // Only a price comparison hits the market board, so repeats don't need the long gap.
            if (!reused) await Task.Delay(ItemDelay, token);
        }
        Log("Done.");
    }

    // Why this item should keep its current price, or null to go ahead with newPrice.
    private static unsafe string? SkipReason(int listIndex, int current, int? newPrice)
    {
        if (newPrice is not { } price) return "no suggested price";
        var item = ListedItem(listIndex);
        var search = InfoProxyItemSearch.Instance();
        if (item == null || search->SearchItemId != item->GetBaseItemId())
            return "couldn't match this item to the price comparison";
        if (Config.SkipIfCheapest && AlreadyCheapest(search, item->IsHighQuality())) return "you already have the cheapest listing";
        if (price >= current) return $"suggested {price:N0} isn't lower";
        return null;
    }

    // True if your cheapest listing of this quality beats everyone else's. A tie counts as not cheapest.
    private static unsafe bool AlreadyCheapest(InfoProxyItemSearch* search, bool hq)
    {
        var me = Player.ContentId;
        uint mine = uint.MaxValue, others = uint.MaxValue;
        foreach (ref readonly var l in search->Listings[..Math.Min((int)search->ListingCount, search->Listings.Length)])
        {
            if (l.IsHqItem != hq) continue;
            if (l.ContentId == me) mine = Math.Min(mine, l.UnitPrice);
            else others = Math.Min(others, l.UnitPrice);
        }
        return mine < others;
    }

    private static unsafe (uint ItemId, bool Hq)? ItemKey(int listIndex)
    {
        var item = ListedItem(listIndex);
        return item == null ? null : (item->GetBaseItemId(), item->IsHighQuality());
    }

    // The item at this position in the sell list. Assumes the list shows listings in slot order.
    private static unsafe InventoryItem* ListedItem(int listIndex)
    {
        var market = InventoryManager.Instance()->GetInventoryContainer(InventoryType.RetainerMarket);
        if (market == null) return null;
        for (var i = 0; i < market->Size; i++)
        {
            var item = market->GetInventorySlot(i);
            if (!item->IsEmpty() && listIndex-- == 0) return item;
        }
        return null;
    }

    private static unsafe int ListingCount()
    {
        var market = InventoryManager.Instance()->GetInventoryContainer(InventoryType.RetainerMarket);
        if (market == null) return 0;
        var n = 0;
        for (var i = 0; i < market->Size; i++)
            if (!market->GetInventorySlot(i)->IsEmpty()) n++;
        return n;
    }

    private static unsafe AtkUnitBase* Addon(string name) => RaptureAtkUnitManager.Instance()->GetAddonByName(name);
    private static unsafe AddonRetainerSell* SellAddon() => (AddonRetainerSell*)Addon("RetainerSell");
    private static unsafe int AskingPrice() => SellAddon()->AskingPrice->Value;

    private static unsafe bool IsReady(string name)
    {
        var a = Addon(name);
        return a != null && a->IsVisible && a->IsReady;
    }

    private static unsafe void SetPriceAndConfirm(int price)
    {
        SellAddon()->AskingPrice->SetValue(price);
        Fire("RetainerSell", SellConfirm);
    }

    private static unsafe void Fire(string name, params int[] args)
    {
        var addon = Addon(name);
        if (addon == null) throw new InvalidOperationException($"{name} is not open.");
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++) values[i].SetInt(args[i]);
        addon->FireCallback((uint)args.Length, values, true);
    }

    private static unsafe void Close(string name)
    {
        var addon = Addon(name);
        if (addon != null && addon->IsVisible) addon->Close(true);
    }

    private static async Task WaitFor(string name, CancellationToken token, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 100)
        {
            if (await Framework.RunOnFrameworkThread(() => IsReady(name)))
            {
                await Task.Delay(StepDelay, token); // let the window finish setting up before touching it
                return;
            }
            await Task.Delay(100, token);
        }
        throw new TimeoutException($"Timed out waiting for {name}.");
    }

    // Waits for the other plugin to put the undercut price on the clipboard. Null if it never shows up.
    private static async Task<int?> WaitForClipboardPrice(CancellationToken token)
    {
        for (var waited = 0; waited < Config.PriceWaitSeconds * 1000; waited += 200)
        {
            await Task.Delay(200, token);
            var text = await Framework.RunOnFrameworkThread(ImGui.GetClipboardText);
            if (ParsePrice(text) is { } price) return price;
        }
        return null;
    }

    // "12,345" / "12.345" / "12345 gil" -> 12345
    internal static int? ParsePrice(string? text)
    {
        var digits = new string((text ?? "").Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var p) && p > 0 ? p : null;
    }
}

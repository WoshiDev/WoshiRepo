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
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using static SubSalvage.Planner;

namespace SubSalvage;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IChatGui Chat { get; set; } = null!;
    [PluginService] private static IDataManager Data { get; set; } = null!;

    public static Configuration Config { get; private set; } = null!;
    // Lines from the latest run, shown in the main window. Lock it before reading or writing.
    public static readonly List<string> Results = [];

    // Salvaged and Extravagant Salvaged Ring, Bracelet, Earring and Necklace, checked against the game data.
    public static readonly uint[] SalvagedItems = [22500, 22501, 22502, 22503, 22504, 22505, 22506, 22507];
    // Gems and ingots from the MOJ and OJ routes: Gold Ingot, Platinum Ingot, Ruby, Star Ruby, Diamond,
    // Emerald, Sapphire, Star Sapphire. Checked against the game data.
    public static readonly uint[] RouteDrops = [5069, 9360, 5187, 12544, 5188, 5189, 5192, 12545];

    private static readonly InventoryType[] Bags =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    // Windows that let you sell to a vendor: a merchant's shop, or a retainer's inventory or sell list.
    private static readonly string[] VendorWindows = ["Shop", "InventoryRetainer", "InventoryRetainerLarge", "RetainerSellList"];
    // Addon sheet rows for the item menu entries: "Sell" (at a shop), "Have Retainer Sell Items", "Split".
    private const uint TextSell = 93, TextRetainerSell = 5480, TextSplit = 92;

    private static TimeSpan StepDelay => TimeSpan.FromMilliseconds(Config.StepDelayMs);

    private readonly WindowSystem windows = new("SubSalvage");
    private readonly MainWindow mainWindow;
    private CancellationTokenSource? cts;

    public bool IsRunning => cts != null;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        mainWindow = new MainWindow(this) { Size = new(420, 480), SizeCondition = ImGuiCond.FirstUseEver };
        windows.AddWindow(mainWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle; // settings are in the main window

        Commands.AddHandler("/salvage", new CommandInfo(OnCommand)
        {
            HelpMessage = "Sell submarine trash to the open merchant or retainer, keeping 1 of each.\n" +
                          "/salvage dry - list what would be sold, changes nothing\n" +
                          "/salvage stop - cancel\n" +
                          "/salvage window - open the main window",
        });
    }

    public void Dispose()
    {
        cts?.Cancel();
        Commands.RemoveHandler("/salvage");
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        windows.RemoveAllWindows();
    }

    public static void SaveConfig() => PluginInterface.SavePluginConfig(Config);

    private void OnCommand(string command, string args)
    {
        switch (args.Trim())
        {
            case "stop": Stop(); break;
            case "window": mainWindow.Toggle(); break;
            case "dry": Start(dryRun: true); break;
            default: Start(dryRun: false); break;
        }
    }

    public void Stop() => cts?.Cancel();

    public void Start(bool dryRun)
    {
        if (IsRunning) { Chat.Print("[Salvage] Already running."); return; }

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
                Chat.PrintError($"[Salvage] {e.Message}");
            }
            finally { cts = null; }
        });
    }

    private static void Log(string message)
    {
        lock (Results) Results.Add(message);
        if (Config.PrintToChat) Chat.Print($"[Salvage] {message}");
    }

    // Your list first, then the MOJ/OJ drops if ticked, then the Salvaged accessories. Items a vendor won't buy are left out.
    private static List<uint> TrashItems() =>
        Config.ExtraItems.Concat(Config.IncludeRouteDrops ? RouteDrops : []).Concat(SalvagedItems).Distinct().Where(CanVendor).ToList();

    public static bool CanVendor(uint itemId) => Data.GetExcelSheet<Item>().TryGetRow(itemId, out var row) && row.PriceLow > 0;
    public static string ItemName(uint itemId) =>
        Data.GetExcelSheet<Item>().TryGetRow(itemId, out var row) ? row.Name.ExtractText() : $"item {itemId}";
    private static string Label(Stack stack) => ItemName(stack.ItemId) + (stack.Hq ? " (HQ)" : "");

    private static async Task Run(bool dryRun, CancellationToken token)
    {
        var steps = await Framework.RunOnFrameworkThread(() => Plan(BagStacks(), TrashItems()));
        if (steps.Count == 0) { Log("Nothing to sell: you have at most 1 of each item and quality."); return; }

        if (dryRun)
        {
            foreach (var s in steps) Log($"(dry run) Would sell {Describe(s)}.");
            Log($"{steps.Count(s => s.Split)} of these need a split, which needs 1 free bag slot.");
            return;
        }

        if (!await Framework.RunOnFrameworkThread(VendorOpen))
            throw new InvalidOperationException("Open a merchant's shop, or a retainer's inventory or sell list, first.");

        foreach (var step in steps)
        {
            token.ThrowIfCancellationRequested();
            var stack = step.Stack;
            if (step.Split)
            {
                if ((await Framework.RunOnFrameworkThread(EmptySlots)).Count == 0)
                    throw new InvalidOperationException(
                        $"Your bags are full, so {Label(stack)} can't be split to keep 1. Free up one bag slot and run again.");
                stack = await Split(stack, token);
            }
            await Sell(stack, token);
            Log($"Sold {stack.Quantity:N0} {Label(stack)}.");
            await Task.Delay(StepDelay, token);
        }
        Log("Done.");
    }

    private static string Describe(Step s) =>
        $"{(s.Split ? s.Stack.Quantity - 1 : s.Stack.Quantity):N0} {Label(s.Stack)}" +
        (s.Split ? $" (splitting it off a stack of {s.Stack.Quantity:N0}, the 1 kept stays in its slot)" : "");

    // Splits all but 1 off into an empty slot, so the 1 you keep stays where the stack was and your bag
    // order doesn't change. Returns the split-off stack, which is the one to sell.
    private static async Task<Stack> Split(Stack stack, CancellationToken token)
    {
        var amount = stack.Quantity - 1;
        var empty = await Framework.RunOnFrameworkThread(EmptySlots);
        await OpenMenu(stack, token);
        await Framework.RunOnFrameworkThread(() => SelectMenuEntry(TextSplit));
        await WaitFor("InputNumeric", token);
        await Framework.RunOnFrameworkThread(() => Fire("InputNumeric", amount));

        Stack? moved = null;
        var split = await WaitUntil(() =>
        {
            moved = empty.Select(e => stack with { Bag = e.Bag, Slot = e.Slot, Quantity = amount }).FirstOrDefault(s => QuantityAt(s) == amount);
            return moved != null && QuantityAt(stack) == 1;
        }, token);
        if (!split)
            throw new InvalidOperationException($"Splitting {amount:N0} off {Label(stack)} didn't leave 1 in its slot and {amount:N0} in an empty one.");
        await Task.Delay(StepDelay, token);
        return moved!;
    }

    // Sells the whole stack in this slot, after checking it still holds what the plan expects.
    private static async Task Sell(Stack stack, CancellationToken token)
    {
        await OpenMenu(stack, token);
        await Framework.RunOnFrameworkThread(() => SelectMenuEntry(TextRetainerSell, TextSell));
        // The game may ask for a quantity or a confirmation first. These are always for the item just picked,
        // and each is answered once so a window that's still closing doesn't get a second click.
        bool askedQuantity = false, confirmed = false;
        var sold = await WaitUntil(() =>
        {
            if (!askedQuantity && IsReady("InputNumeric")) { Fire("InputNumeric", stack.Quantity); askedQuantity = true; }
            else if (!confirmed && IsReady("SelectYesno")) { Fire("SelectYesno", 0); confirmed = true; }
            return QuantityAt(stack) == 0;
        }, token);
        if (!sold) throw new InvalidOperationException($"{Label(stack)} didn't sell. Open windows: {await Framework.RunOnFrameworkThread(OpenWindows)}");
    }

    private static async Task OpenMenu(Stack stack, CancellationToken token)
    {
        var quantity = await Framework.RunOnFrameworkThread(() => QuantityAt(stack));
        if (quantity != stack.Quantity)
            throw new InvalidOperationException($"{Label(stack)} moved or changed in your bags. Run again.");
        await Framework.RunOnFrameworkThread(() => OpenMenuFor(stack));
        await WaitFor("ContextMenu", token);
    }

    // Every non-empty stack in the main bags.
    public static unsafe List<Stack> BagStacks()
    {
        var stacks = new List<Stack>();
        foreach (var bag in Bags)
        {
            var container = InventoryManager.Instance()->GetInventoryContainer(bag);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (!item->IsEmpty()) stacks.Add(new Stack((uint)bag, i, item->GetBaseItemId(), item->IsHighQuality(), (int)item->Quantity));
            }
        }
        return stacks;
    }

    // How many of the stack's item, in its quality, are in its slot now. 0 once it's sold or gone.
    private static unsafe int QuantityAt(Stack stack)
    {
        var item = InventoryManager.Instance()->GetInventoryContainer((InventoryType)stack.Bag)->GetInventorySlot(stack.Slot);
        return item->IsEmpty() || item->GetBaseItemId() != stack.ItemId || item->IsHighQuality() != stack.Hq ? 0 : (int)item->Quantity;
    }

    private static unsafe List<(uint Bag, int Slot)> EmptySlots()
    {
        var empty = new List<(uint, int)>();
        foreach (var bag in Bags)
        {
            var container = InventoryManager.Instance()->GetInventoryContainer(bag);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
                if (container->GetInventorySlot(i)->IsEmpty()) empty.Add(((uint)bag, i));
        }
        return empty;
    }

    public static bool VendorOpen() => VendorWindows.Any(IsReady);

    private static unsafe void OpenMenuFor(Stack stack)
    {
        var inventoryAgent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory);
        AgentInventoryContext.Instance()->OpenForItemSlot((InventoryType)stack.Bag, stack.Slot, 0, inventoryAgent->GetAddonId());
    }

    // Picks the first menu entry whose text is one of these Addon rows, matched by the game's own text so any
    // client language works. Exact match, since "Have Retainer Sell Items" contains "Sell".
    // The callback takes the entry's position in the menu, counted from the first entry text.
    private static unsafe void SelectMenuEntry(params uint[] textIds)
    {
        var sheet = Data.GetExcelSheet<Lumina.Excel.Sheets.Addon>();
        var texts = textIds.Select(id => sheet.GetRow(id).Text.ExtractText()).ToArray();

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
            entries.Add(value.String.ToString().Trim());
        }
        foreach (var text in texts)
        {
            var index = entries.IndexOf(text);
            if (index < 0) continue;
            Fire("ContextMenu", 0, index, 0);
            return;
        }
        menu->Close(true);
        throw new InvalidOperationException($"No \"{string.Join("\" or \"", texts)}\" in the item menu. It had: {string.Join(", ", entries)}");
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

    private static unsafe AtkUnitBase* Addon(string name) => RaptureAtkUnitManager.Instance()->GetAddonByName(name);

    private static unsafe bool IsReady(string name)
    {
        var a = Addon(name);
        return a != null && a->IsVisible && a->IsReady;
    }

    private static unsafe void Fire(string name, params int[] args)
    {
        var addon = Addon(name);
        if (addon == null) throw new InvalidOperationException($"{name} is not open.");
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++) values[i].SetInt(args[i]);
        addon->FireCallback((uint)args.Length, values, true);
    }

    private static async Task WaitFor(string name, CancellationToken token, int timeoutMs = 5000)
    {
        if (!await WaitUntil(() => IsReady(name), token, timeoutMs)) throw new TimeoutException($"Timed out waiting for {name}.");
        await Task.Delay(StepDelay, token); // let the window finish setting up before touching it
    }

    // Checks the condition on the game thread until it's true. False if it never was.
    private static async Task<bool> WaitUntil(Func<bool> condition, CancellationToken token, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 100)
        {
            if (await Framework.RunOnFrameworkThread(condition)) return true;
            await Task.Delay(100, token);
        }
        return false;
    }
}

using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace RetainerUndercut;

public sealed class MainWindow(Plugin plugin) : Window("Retainer Undercut###RetainerUndercutMain")
{
    private (uint ItemId, bool Hq)? selected;
    private int stacks = 1;

    public override void Draw()
    {
        if (plugin.IsRunning) ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Running...");
        else ImGui.TextWrapped("Open a retainer's sell list at a summoning bell first.");

        ImGui.BeginDisabled(!plugin.IsRunning);
        if (ImGui.Button("Stop")) plugin.Stop();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Settings")) plugin.ConfigWindow.Toggle();

        ImGui.Separator();
        ImGui.TextDisabled("Reprice this retainer's listings");
        ImGui.BeginDisabled(plugin.IsRunning);
        if (ImGui.Button("Run")) plugin.Start(dryRun: false);
        ImGui.SameLine();
        if (ImGui.Button("Dry run")) plugin.Start(dryRun: true);
        ImGui.EndDisabled();

        ImGui.Separator();
        DrawListing();

        ImGui.Separator();
        ImGui.TextDisabled("Last run");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy log"))
            lock (Plugin.Results) ImGui.SetClipboardText(string.Join("\n", Plugin.Results));
        if (ImGui.BeginChild("results", new Vector2(0, 0), true, ImGuiWindowFlags.None))
        {
            lock (Plugin.Results)
                foreach (var line in Plugin.Results) ImGui.TextWrapped(line);
        }
        ImGui.EndChild();
    }

    private void DrawListing()
    {
        ImGui.TextDisabled($"List food & potions ({Plugin.ListingStackSize} per stack, 1 gil under the cheapest)");

        var items = Plugin.SellableItems();
        var current = items.FirstOrDefault(i => (i.ItemId, i.Hq) == selected);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##item", current == null ? "Choose food or a potion..." : Label(current)))
        {
            foreach (var item in items)
                if (ImGui.Selectable(Label(item), item == current)) selected = (item.ItemId, item.Hq);
            if (items.Count == 0) ImGui.TextDisabled("No food or potions in your bags.");
            ImGui.EndCombo();
        }

        var open = Plugin.SellListOpen();
        var free = open ? Plugin.FreeListingSlots() : 0;
        var max = Math.Min(current?.FullStacks ?? 0, free);
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("Stacks", ref stacks);
        stacks = Math.Clamp(stacks, 1, Math.Max(1, max));
        ImGui.SameLine();
        ImGui.TextDisabled(open ? $"{free} free market slot{(free == 1 ? "" : "s")}" : "Open a retainer's sell list");

        ImGui.BeginDisabled(plugin.IsRunning || current == null || max == 0);
        if (ImGui.Button("List for sale")) plugin.StartListing(current!, stacks, dryRun: false);
        ImGui.SameLine();
        if (ImGui.Button("Dry run##list")) plugin.StartListing(current!, stacks, dryRun: true);
        ImGui.EndDisabled();
    }

    private static string Label(Plugin.SellableItem item) =>
        $"{item.Name}{(item.Hq ? " (HQ)" : "")}  -  {item.Quantity:N0} ({item.FullStacks} full stack{(item.FullStacks == 1 ? "" : "s")})";
}

public sealed class ConfigWindow() : Window("Retainer Undercut Settings###RetainerUndercutConfig", ImGuiWindowFlags.AlwaysAutoResize)
{
    public override void Draw()
    {
        var c = Plugin.Config;
        var changed = false;

        ImGui.SetNextItemWidth(200);
        if (ImGui.InputInt("Undercut by (gil)", ref c.UndercutAmount))
        {
            c.UndercutAmount = Math.Max(0, c.UndercutAmount);
            changed = true;
        }
        ImGui.TextDisabled("0 matches the cheapest listing instead of undercutting it.");
        changed |= ImGui.Checkbox("Print results to chat", ref c.PrintToChat);

        ImGui.Separator();
        ImGui.TextDisabled("Timing (raise these if steps get skipped or the market board says to wait)");
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("Delay between items (ms)", ref c.ItemDelayMs, 50, 2000);
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("Delay between steps (ms)", ref c.StepDelayMs, 200, 3000);
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("Wait for market data (s)", ref c.PriceWaitSeconds, 3, 30);

        if (changed) Plugin.SaveConfig();
    }
}

using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SubSalvage;

public sealed class MainWindow(Plugin plugin) : Window("Sub Salvage###SubSalvageMain")
{
    public override void Draw()
    {
        if (plugin.IsRunning) ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Running...");
        else if (Plugin.VendorOpen()) ImGui.TextWrapped("Ready to sell.");
        else ImGui.TextWrapped("Open a merchant's shop, or a retainer's inventory or sell list, to sell.");

        ImGui.BeginDisabled(plugin.IsRunning);
        if (ImGui.Button("Sell")) plugin.Start(dryRun: false);
        ImGui.SameLine();
        if (ImGui.Button("Dry run")) plugin.Start(dryRun: true);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!plugin.IsRunning);
        if (ImGui.Button("Stop")) plugin.Stop();
        ImGui.EndDisabled();

        ImGui.Separator();
        DrawItems();

        ImGui.Separator();
        var c = Plugin.Config;
        var changed = ImGui.Checkbox("Print results to chat", ref c.PrintToChat);
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("Delay between steps (ms)", ref c.StepDelayMs, 200, 3000);
        if (changed) Plugin.SaveConfig();

        ImGui.Separator();
        ImGui.TextDisabled("Last run");
        if (ImGui.BeginChild("results", new Vector2(0, 0), true, ImGuiWindowFlags.None))
        {
            lock (Plugin.Results)
                foreach (var line in Plugin.Results) ImGui.TextWrapped(line);
        }
        ImGui.EndChild();
    }

    private static void DrawItems()
    {
        ImGui.TextDisabled("1 of each item is always kept, NQ and HQ separately.");
        ImGui.TextDisabled("Always sold: Salvaged and Extravagant Salvaged accessories");

        var c = Plugin.Config;
        if (ImGui.Checkbox("Include default MOJ/OJ drops", ref c.IncludeRouteDrops)) Plugin.SaveConfig();
        ImGui.Indent();
        ImGui.TextDisabled(string.Join(", ", Plugin.RouteDrops.Select(Plugin.ItemName)));
        ImGui.Unindent();

        ImGui.TextDisabled("Your items (sold first):");
        var list = c.ExtraItems;
        for (var i = 0; i < list.Count; i++)
        {
            if (ImGui.SmallButton($"Remove##{i}")) { list.RemoveAt(i); Plugin.SaveConfig(); break; }
            ImGui.SameLine();
            ImGui.TextUnformatted(Plugin.ItemName(list[i]));
        }
        if (list.Count == 0) ImGui.TextDisabled("  (none yet)");

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##add", "Add an item from your bags..."))
        {
            var candidates = Plugin.BagStacks().Select(s => s.ItemId).Distinct()
                .Where(id => !list.Contains(id) && !Plugin.SalvagedItems.Contains(id) &&
                             !(c.IncludeRouteDrops && Plugin.RouteDrops.Contains(id)) && Plugin.CanVendor(id))
                .Select(id => (Id: id, Name: Plugin.ItemName(id))).OrderBy(e => e.Name).ToList();
            foreach (var (id, name) in candidates)
                if (ImGui.Selectable($"{name}##{id}")) { list.Add(id); Plugin.SaveConfig(); }
            if (candidates.Count == 0) ImGui.TextDisabled("Nothing else in your bags can be sold to a vendor.");
            ImGui.EndCombo();
        }
    }
}

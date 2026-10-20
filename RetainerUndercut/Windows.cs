using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace RetainerUndercut;

public sealed class MainWindow(Plugin plugin) : Window("Retainer Undercut###RetainerUndercutMain")
{
    public override void Draw()
    {
        if (plugin.IsRunning) ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Running...");
        else ImGui.TextWrapped("Open a retainer's sell list at a summoning bell, then press Run.");

        ImGui.BeginDisabled(plugin.IsRunning);
        if (ImGui.Button("Run")) plugin.Start(dryRun: false);
        ImGui.SameLine();
        if (ImGui.Button("Dry run")) plugin.Start(dryRun: true);
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!plugin.IsRunning);
        if (ImGui.Button("Stop")) plugin.Stop();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Settings")) plugin.ConfigWindow.Toggle();

        ImGui.Separator();
        ImGui.TextDisabled("Last run");
        if (ImGui.BeginChild("results", new Vector2(0, 0), true, ImGuiWindowFlags.None))
        {
            lock (Plugin.Results)
                foreach (var line in Plugin.Results) ImGui.TextWrapped(line);
        }
        ImGui.EndChild();
    }
}

public sealed class ConfigWindow() : Window("Retainer Undercut Settings###RetainerUndercutConfig", ImGuiWindowFlags.AlwaysAutoResize)
{
    public override void Draw()
    {
        var c = Plugin.Config;
        var changed = false;

        changed |= ImGui.Checkbox("Skip items where you already have the cheapest listing", ref c.SkipIfCheapest);
        changed |= ImGui.Checkbox("Print results to chat", ref c.PrintToChat);

        ImGui.Separator();
        ImGui.TextDisabled("Timing (raise these if steps get skipped or the market board says to wait)");
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("Delay between items (ms)", ref c.ItemDelayMs, 1000, 10000);
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("Delay between steps (ms)", ref c.StepDelayMs, 200, 3000);
        ImGui.SetNextItemWidth(200);
        changed |= ImGui.SliderInt("Wait for suggested price (s)", ref c.PriceWaitSeconds, 3, 30);

        if (changed) Plugin.SaveConfig();
    }
}

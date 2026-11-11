using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace SubSalvage;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Your own trash (gems, ingots...), sold before the built-in Salvaged accessories.
    public List<uint> ExtraItems = [];
    // Sell the built-in MOJ/OJ gems and ingots too, after your own items.
    public bool IncludeRouteDrops = true;
    public bool PrintToChat = true;
    public int StepDelayMs = 600;
}

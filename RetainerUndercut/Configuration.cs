using System;
using Dalamud.Configuration;

namespace RetainerUndercut;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool SkipIfCheapest = true;
    public bool PrintToChat = true;
    public int ItemDelayMs = 2500;
    public int StepDelayMs = 600;
    public int PriceWaitSeconds = 10;
}

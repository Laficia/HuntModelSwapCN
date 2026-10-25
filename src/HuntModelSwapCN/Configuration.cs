using Dalamud.Configuration;
using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool CaptureHuntDialogue { get; set; } = true;
    public List<HuntDialogue> CapturedDialogue { get; set; } = [];
}

using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

// Native name-icon resource IDs, verified against the current CN client selection table.
// These are UI assets, never character aggression flags or native function addresses.
public static class NameplateIconPolicy
{
    public const int PassiveElite = 61704;
    public const int AggressiveElite = 61710;

    public static int? Resolve(SwapEntry? entry, IReadOnlyList<HuntAppearance> catalog)
    {
        if (entry is null || entry.Restoring || entry.WaitingForDraw) return null;
        var hunt = catalog.FirstOrDefault(h => h.Name == entry.ReplacementName && h.ModelId == entry.Applied.ModelId
            && h.NameplateIcon == entry.Applied.NameplateIcon);
        // All current regular hunts have native icon category 1. Do not guess unknown future shapes.
        if (hunt is null || hunt.NameplateIcon != 1 || hunt.Rank is < 1 or > 3) return null;
        return hunt.IsAggressive ? AggressiveElite : PassiveElite;
    }
}

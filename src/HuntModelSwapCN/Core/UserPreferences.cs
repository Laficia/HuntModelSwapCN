namespace HuntModelSwapCN.Core;

// Persist values only. Actor identities, pointers, hiding and pause leases are session-only.
public sealed record UserPreferences
{
    public uint SelectedNameId { get; init; }
    public uint SelectedBaseId { get; init; }
    public int Rank { get; init; }
    public int Expansion { get; init; } = -1;
    public uint Territory { get; init; }
    public string Search { get; init; } = "";
    public float SpawnDistance { get; init; } = 8;
    public bool FloatingControls { get; init; }
    public ModelStance Stance { get; init; }
    public Dictionary<string, float> Scales { get; init; } = [];
    public Dictionary<string, bool> Sections { get; init; } = [];
    public static string HuntKey(uint nameId, uint baseId) => $"{nameId}:{baseId}";

    public UserPreferences Normalize() => this with {
        Rank = Rank is >= 0 and <= 5 ? Rank : 0,
        Expansion = Expansion is >= -1 and <= 10 ? Expansion : -1,
        Search = (Search ?? "")[..Math.Min((Search ?? "").Length, 128)],
        SpawnDistance = float.IsFinite(SpawnDistance) ? Math.Clamp(SpawnDistance, 2, 30) : 8,
        Stance = Enum.IsDefined(Stance) ? Stance : ModelStance.Automatic,
        Scales = (Scales ?? []).Where(p => ValidKey(p.Key) && float.IsFinite(p.Value) && p.Value > 0)
            .Take(1024).ToDictionary(p => p.Key, p => p.Value),
        Sections = (Sections ?? []).Where(p => p.Key is "modelControls" or "visibilityControls" or "hiddenTargets"
            or "swappedActors" or "help" or "dialogue" or "skills").ToDictionary(p => p.Key, p => p.Value),
    };

    private static bool ValidKey(string key)
    {
        var parts = key.Split(':');
        return parts.Length == 2 && uint.TryParse(parts[0], out var n) && n > 0 && uint.TryParse(parts[1], out var b) && b > 0;
    }
}

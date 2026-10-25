namespace HuntModelSwapCN.Core;

public static class NameplateLevelPolicy
{
    // The CN enemy-nameplate prefix is "90级 ". Updating Character.Level alone
    // does not guarantee that an existing cached nameplate rebuilds this field.
    public static string? Resolve(SwapEntry? entry)
    {
        if (entry is null) return null;
        var level = entry.Restoring ? entry.Original.Level : entry.Applied.Level;
        return level > 0 ? $"{level}级 " : null;
    }
}

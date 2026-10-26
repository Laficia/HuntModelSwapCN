namespace HuntModelSwapCN.Core;

public static class NameplateLevelPolicy
{
    // CN UpdateBattleCharaNameplates uses U+E060-based digits and Addon 2034:
    // <string(lstr1)>级<nbsp>. ASCII digits/spaces select different glyphs/spacing.
    public const char DigitZero = '\uE060';
    private static readonly ReadOnlyMemory<byte>[] Prefixes = Enumerable.Range(0, 256).Select(level => {
        var digits = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var text = new string(digits.Select(c => (char)(DigitZero + c - '0')).ToArray()) + "级";
        return new ReadOnlyMemory<byte>([.. System.Text.Encoding.UTF8.GetBytes(text), 0x02, 0x1D, 0x01, 0x03]);
    }).ToArray();

    public static ReadOnlyMemory<byte>? Resolve(SwapEntry? entry)
    {
        if (entry is null) return null;
        var level = entry.Restoring ? entry.Original.Level : entry.Applied.Level;
        if (level == 0) return null;
        return Prefixes[level];
    }
}

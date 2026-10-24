using System.Globalization;

namespace HuntModelSwapCN.Core;

public static class PreviewCommand
{
    public const string Usage = "用法：/hms play 技能ID（Action ID，不是时间轴 ID）。";
    public static bool IsPlay(string arguments) => arguments.Trim().Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Equals("play", StringComparison.OrdinalIgnoreCase) == true;

    public static uint Parse(string arguments)
    {
        var parts = arguments.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("play", StringComparison.OrdinalIgnoreCase)
            || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            throw new InvalidOperationException(Usage);
        return id;
    }
}

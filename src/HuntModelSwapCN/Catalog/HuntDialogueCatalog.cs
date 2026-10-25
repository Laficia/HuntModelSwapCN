using System.Text;
using System.Text.Json;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace HuntModelSwapCN.Catalog;

public sealed record HuntDialogue(uint NameId, string Text, float Seconds, uint RowId = 0);
public sealed record HuntDialogueRow(uint RowId, string ExpectedText);
public sealed record HuntDialogueProfile(uint NameId, string Name, int ModelId, string WikiUrl, HuntDialogueRow[] Rows);

// NpcYell.Name is zero for these rows: ownership must never be inferred from adjacent row IDs.
public sealed class HuntDialogueCatalog(IReadOnlyList<HuntAppearance> hunts)
{
    public const uint Atticus = 13156;
    public const int MaxPerHunt = 128;
    public static readonly IReadOnlyList<HuntDialogueProfile> Profiles = LoadProfiles();
    private readonly Dictionary<string, uint> speakers = hunts.Where(h => h.Rank == 3)
        .ToDictionary(h => h.Name, h => h.NameId, StringComparer.Ordinal);
    private readonly List<HuntDialogue> lines = [];
    public HuntDialogue[] All => lines.ToArray();
    public uint? Speaker(string name) => speakers.TryGetValue(name.Trim(), out var id) ? id : null;

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("台词不能为空。");
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length == 0 || Encoding.UTF8.GetByteCount(normalized) > 2048
            || normalized.Any(c => char.IsControl(c) && c != '\n'))
            throw new InvalidOperationException("台词不能为空、含控制字符或超过 2048 字节。");
        return normalized;
    }

    public bool Capture(uint nameId, string text)
    {
        if (!speakers.ContainsValue(nameId)) return false;
        text = Normalize(text);
        if (lines.Any(l => l.NameId == nameId && l.Text == text)
            || lines.Count(l => l.NameId == nameId) >= MaxPerHunt) return false;
        lines.Add(new(nameId, text, 5));
        return true;
    }

    public void LoadBuiltIns(ExcelSheet<NpcYell> rows)
    {
        foreach (var profile in Profiles)
        {
            if (!hunts.Any(h => h.Rank == 3 && h.NameId == profile.NameId && h.Name == profile.Name && h.ModelId == profile.ModelId)) continue;
            foreach (var mapped in profile.Rows)
            {
                var row = rows.GetRowOrDefault(mapped.RowId);
                if (row is null || !Matches(mapped, row.Value.Text.ExtractText())) continue;
                // Identical spoken text is shown once, even when native directional row IDs differ.
                if (lines.Any(l => l.NameId == profile.NameId && l.Text == mapped.ExpectedText)) continue;
                lines.Add(new(profile.NameId, mapped.ExpectedText,
                    row.Value.BalloonTime is >= 2 and <= 15 ? row.Value.BalloonTime : 5, mapped.RowId));
            }
        }
    }

    public static bool Matches(HuntDialogueRow mapped, string text)
    {
        try { return Normalize(text) == mapped.ExpectedText; }
        catch (InvalidOperationException) { return false; }
    }

    private static HuntDialogueProfile[] LoadProfiles()
    {
        using var stream = typeof(HuntDialogueCatalog).Assembly.GetManifestResourceStream("HuntModelSwapCN.SRankDialogueProfiles.json")
            ?? throw new InvalidOperationException("缺少 S 怪台词核对资料。");
        return JsonSerializer.Deserialize<HuntDialogueProfile[]>(stream)
            ?? throw new InvalidOperationException("S 怪台词资料无法读取。");
    }
}

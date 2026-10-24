using Lumina.Excel;
using Lumina.Excel.Sheets;
using GameAction = Lumina.Excel.Sheets.Action;

namespace HuntModelSwapCN.Catalog;

public sealed record SkillAnimation(uint ActionId, string Name, ushort TimelineId, string TimelineKey, byte Slot, int CastTimeMs = 0)
{
    // This identifies animation resources, not which skills a particular hunt actually owns.
    public string Family => TimelineKey.StartsWith("mon_sp/", StringComparison.Ordinal)
        ? TimelineKey.Split('/').ElementAtOrDefault(1) ?? "" : "";
    public bool Matches(string query) => string.IsNullOrWhiteSpace(query)
        || ActionId.ToString() == query.Trim()
        || Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
        || TimelineKey.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class SkillCatalog
{
    public static IReadOnlyDictionary<uint, SkillAnimation> Load(ExcelSheet<GameAction> actions,
        ExcelSheet<ActionTimeline> timelines)
    {
        Dictionary<uint, SkillAnimation> result = [];
        foreach (var action in actions)
        {
            var id = action.AnimationEnd.RowId;
            // Resolve explicitly: RowRef.Value may use a different sheet language on CN.
            if (action.RowId == 0 || action.IsPlayerAction || id is 0 or > ushort.MaxValue
                || !timelines.TryGetRow(id, out var timeline) || timeline.Slot >= 14) continue;
            var key = timeline.Key.ToString();
            if (string.IsNullOrWhiteSpace(key)) continue;
            var name = action.Name.ToString();
            result.Add(action.RowId, new(action.RowId, string.IsNullOrWhiteSpace(name) ? "未命名技能" : name,
                (ushort)id, key, timeline.Slot, action.Cast100ms * 100));
        }
        return result;
    }

    public static string ModelFamily(ModelChara model) => model.Type switch
    {
        2 => $"d{model.Model:D4}",
        3 => $"m{model.Model:D4}",
        _ => "", // Human animation compatibility cannot be inferred from ModelChara alone.
    };
}

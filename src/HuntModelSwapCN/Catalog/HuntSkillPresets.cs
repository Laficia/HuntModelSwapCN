using System.Text.Json;

namespace HuntModelSwapCN.Catalog;

public sealed record SkillMapping(uint ActionId, ushort TimelineId, string TimelineKey, string? ActionName = null);
public sealed record HuntSkillEntry(string Name, string Mode, SkillMapping? Primary, SkillMapping[] Variants);
public sealed record HuntSkillProfile(uint NameId, string Name, int ModelId, string WikiUrl,
    string? RecordSource, HuntSkillEntry[] Skills);

// Wiki skill ownership is separate from the confidence of each local animation mapping.
public static class HuntSkillPresets
{
    public const uint BufruNameId = 10617;
    public const string BufruSource = "https://ff14.huijiwiki.com/wiki/布弗鲁";
    private static readonly Dictionary<uint, HuntSkillProfile> Profiles = Load();
    public static IReadOnlyCollection<HuntSkillProfile> All => Profiles.Values;
    public static HuntSkillProfile? Profile(uint nameId) => Profiles.GetValueOrDefault(nameId);

    private static Dictionary<uint, HuntSkillProfile> Load()
    {
        using var stream = typeof(HuntSkillPresets).Assembly.GetManifestResourceStream("HuntModelSwapCN.SRankSkillProfiles.json")
            ?? throw new InvalidOperationException("缺少 S 怪专属技能资料。");
        using var additional = typeof(HuntSkillPresets).Assembly.GetManifestResourceStream("HuntModelSwapCN.AdditionalHuntSkillProfiles.json")
            ?? throw new InvalidOperationException("缺少 A/B/特殊狩猎技能资料。");
        return (JsonSerializer.Deserialize<HuntSkillProfile[]>(stream)
            ?? throw new InvalidOperationException("S 怪技能资料无法读取。"))
            .Concat(JsonSerializer.Deserialize<HuntSkillProfile[]>(additional)
                ?? throw new InvalidOperationException("A/B/特殊狩猎技能资料无法读取。"))
            .ToDictionary(p => p.NameId);
    }

    public static SkillAnimation? Resolve(string name, SkillMapping? mapping, IReadOnlyDictionary<uint, SkillAnimation> skills)
        => mapping is not null && skills.TryGetValue(mapping.ActionId, out var skill) && skill.Name == (mapping.ActionName ?? name)
            && skill.TimelineId == mapping.TimelineId && skill.TimelineKey == mapping.TimelineKey ? skill : null;

    public static IEnumerable<SkillAnimation> For(uint nameId, IReadOnlyDictionary<uint, SkillAnimation> skills)
        => Profile(nameId)?.Skills.Select(entry => Resolve(entry.Name, entry.Primary, skills)).OfType<SkillAnimation>() ?? [];
}

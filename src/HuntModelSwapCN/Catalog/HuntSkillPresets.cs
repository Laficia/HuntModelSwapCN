namespace HuntModelSwapCN.Catalog;

// Skill names/ownership: the user's Huiji Wiki excerpt; IDs resolved against the installed CN Action sheet.
// These are individual animation previews, not a script reproducing the encounter's mechanics.
public static class HuntSkillPresets
{
    public const uint BufruNameId = 10617;
    public const string BufruSource = "https://ff14.huijiwiki.com/wiki/布弗鲁";
    private static readonly uint[] BufruActions = [27316, 27307, 27309, 27314, 27313];

    public static IEnumerable<SkillAnimation> For(uint nameId, IReadOnlyDictionary<uint, SkillAnimation> skills)
        => nameId == BufruNameId ? BufruActions.Where(skills.ContainsKey).Select(id => skills[id]) : [];
}

using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace HuntModelSwapCN.Catalog;

public sealed record HuntAppearance(uint NameId, uint BaseId, int ModelId, byte Rank, uint Expansion,
    uint TerritoryId, string Name, string Territory, float Scale, byte ModelType,
    byte[] Customize, ulong[] Equipment, ulong MainHand, ulong OffHand, bool Visor, byte NameplateIcon = 1,
    byte Battalion = 0)
{
    public string RankName => Rank switch { 1 => "B", 2 => "A", 3 => "S", _ => "?" };
    public string VersionName => $"{Expansion + 2}.0";
    // Regular B/A/S marks are at their expansion's level cap. These tables contain no level field.
    // Unknown future expansions remain unavailable for level replacement until explicitly mapped.
    public byte Level => Expansion switch { 0 => 50, 1 => 60, 2 => 70, 3 => 80, 4 => 90, 5 => 100, _ => 0 };
}

public static class HuntCatalog
{
    // The first five territory entries are the regular B/A/S hunts. Subsequent entries
    // contain SS minions and SS bosses (whose Rank values also say B/S).
    public static IReadOnlyList<HuntAppearance> Load(ExcelSheet<TerritoryType> territories,
        ExcelSheet<BNpcName> names, ExcelSheet<PlaceName> places)
    {
        var result = new Dictionary<(uint, uint), HuntAppearance>();
        foreach (var territory in territories)
        {
            if (territory.NotoriousMonsterTerritory.RowId == 0) continue;
            var zone = places.GetRowOrDefault(territory.PlaceName.RowId)?.Name.ToString() ?? $"地图 {territory.RowId}";
            foreach (var link in territory.NotoriousMonsterTerritory.Value.NotoriousMonsters.Take(5))
            {
                if (link.RowId == 0 || link.ValueNullable is not { } hunt || hunt.Rank is < 1 or > 3
                    || hunt.BNpcName.RowId == 0 || hunt.BNpcBase.RowId == 0) continue;
                var npc = hunt.BNpcBase.Value;
                var model = npc.ModelChara.ValueNullable;
                // ModelChara row 0 is valid: human hunts such as Albin use Customize + NpcEquip.
                if (model is null || model.Value.Type is < 1 or > 3) continue;
                var name = names.GetRowOrDefault(hunt.BNpcName.RowId)?.Singular.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var equipment = npc.NpcEquip.Value;
                var custom = npc.BNpcCustomize.Value;
                byte[] customize = [
                    (byte)custom.Race.RowId, custom.Gender, custom.BodyType, custom.Height,
                    (byte)custom.Tribe.RowId, custom.Face, custom.HairStyle, custom.HairHighlight,
                    custom.SkinColor, custom.EyeHeterochromia, custom.HairColor, custom.HairHighlightColor,
                    custom.FacialFeature, custom.FacialFeatureColor, custom.Eyebrows, custom.EyeColor,
                    custom.EyeShape, custom.Nose, custom.Jaw, custom.Mouth, custom.LipColor,
                    custom.BustOrTone1, custom.ExtraFeature1, custom.ExtraFeature2OrBust,
                    custom.FacePaint, custom.FacePaintColor];
                ulong[] armor = [
                    Armor(equipment.ModelHead, equipment.DyeHead.RowId, equipment.Dye2Head.RowId),
                    Armor(equipment.ModelBody, equipment.DyeBody.RowId, equipment.Dye2Body.RowId),
                    Armor(equipment.ModelHands, equipment.DyeHands.RowId, equipment.Dye2Hands.RowId),
                    Armor(equipment.ModelLegs, equipment.DyeLegs.RowId, equipment.Dye2Legs.RowId),
                    Armor(equipment.ModelFeet, equipment.DyeFeet.RowId, equipment.Dye2Feet.RowId),
                    Armor(equipment.ModelEars, equipment.DyeEars.RowId, equipment.Dye2Ears.RowId),
                    Armor(equipment.ModelNeck, equipment.DyeNeck.RowId, equipment.Dye2Neck.RowId),
                    Armor(equipment.ModelWrists, equipment.DyeWrists.RowId, equipment.Dye2Wrists.RowId),
                    Armor(equipment.ModelRightRing, equipment.DyeRightRing.RowId, equipment.Dye2RightRing.RowId),
                    Armor(equipment.ModelLeftRing, equipment.DyeLeftRing.RowId, equipment.Dye2LeftRing.RowId)];
                result.TryAdd((hunt.BNpcName.RowId, npc.RowId), new HuntAppearance(hunt.BNpcName.RowId,
                    npc.RowId, (int)model.Value.RowId, hunt.Rank, territory.ExVersion.RowId,
                    territory.RowId, name, zone, npc.Scale, model.Value.Type, customize, armor,
                    Weapon(equipment.ModelMainHand, equipment.DyeMainHand.RowId, equipment.Dye2MainHand.RowId),
                    Weapon(equipment.ModelOffHand, equipment.DyeOffHand.RowId, equipment.Dye2OffHand.RowId),
                    equipment.VisorToggled, npc.Rank, checked((byte)npc.Battalion.RowId)));
            }
        }
        return result.Values.OrderByDescending(h => h.Expansion).ThenByDescending(h => h.Rank)
            .ThenBy(h => h.TerritoryId).ThenBy(h => h.NameId).ToArray();
    }

    private static ulong Armor(uint model, uint dye1, uint dye2)
        => (model & 0x00ffffffUL) | ((dye1 & 0xffUL) << 24) | ((dye2 & 0xffUL) << 32);
    private static ulong Weapon(ulong model, uint dye1, uint dye2)
        => (model & 0x0000ffffffffffffUL) | ((dye1 & 0xffUL) << 48) | ((dye2 & 0xffUL) << 56);
}

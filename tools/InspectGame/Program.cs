using Lumina;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

if (args.Length != 1) throw new ArgumentException("Pass the game's sqpack directory.");
Console.WriteLine("NPC kinds: " + string.Join(", ", Enum.GetNames<Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind>()));
using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
var monsters = game.Excel.GetSheet<NotoriousMonster>()!;
var names = game.Excel.GetSheet<BNpcName>(Lumina.Data.Language.ChineseSimplified)!;
Console.WriteLine("Ranks: " + string.Join(", ", monsters.GroupBy(m => m.Rank).Select(g => $"{g.Key}={g.Count()}")));
foreach (var m in monsters.Where(m => m.BNpcName.RowId != 0).Take(8))
    Console.WriteLine($"{m.BNpcName.RowId}: {names.GetRow(m.BNpcName.RowId).Singular} rank={m.Rank} base={m.BNpcBase.RowId} model={m.BNpcBase.Value.ModelChara.RowId} scale={m.BNpcBase.Value.Scale}");
var territories = game.Excel.GetSheet<TerritoryType>(Lumina.Data.Language.ChineseSimplified)!;
foreach (var territory in territories.Where(t => t.NotoriousMonsterTerritory.RowId != 0))
    foreach (var link in territory.NotoriousMonsterTerritory.Value.NotoriousMonsters.Take(5).Where(l => l.RowId != 0))
    {
        var m = link.Value;
        var model = m.BNpcBase.Value.ModelChara.Value;
        if (model.Type is < 1 or > 3)
            Console.WriteLine($"Excluded: {names.GetRow(m.BNpcName.RowId).Singular} nameId={m.BNpcName.RowId} rank={m.Rank} base={m.BNpcBase.RowId} model={model.RowId} type={model.Type}");
    }
foreach (var territory in territories.Where(t => t.RowId is 134 or 813 or 1187))
{
    Console.WriteLine($"Territory {territory.RowId} expansion={territory.ExVersion.RowId}");
    var list = territory.NotoriousMonsterTerritory.Value;
    foreach (var link in list.NotoriousMonsters.Where(n => n.RowId != 0))
    {
        var m = link.Value;
        Console.WriteLine($"{names.GetRow(m.BNpcName.RowId).Singular}: rank={m.Rank} unknown0={m.Unknown0} baseRank={m.BNpcBase.Value.Rank} modelType={m.BNpcBase.Value.ModelChara.Value.Type}");
    }
}
foreach (var type in new[] { typeof(DrawObjectData), typeof(WeaponModelId) })
{
    Console.WriteLine(type.FullName);
    Console.WriteLine(string.Join(", ", type.GetFields().Where(f => !f.IsStatic).Select(f => $"{f.Name}:{f.FieldType.Name}")));
    Console.WriteLine(string.Join(", ", type.GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
}

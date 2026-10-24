using Lumina;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

if (args.Length == 2 && args[1] == "--nameplate-height")
{
    var objectType = typeof(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject);
    foreach (var field in objectType.GetFields().Where(f => !f.IsStatic
        && new[] { "Height", "Scale", "Nameplate", "DrawOffset", "DrawObject" }.Any(n => f.Name.Contains(n))))
        Console.WriteLine($"{field.Name}: {field.FieldType.Name} offset={System.Runtime.InteropServices.Marshal.OffsetOf(objectType, field.Name):X}");
    foreach (var type in new[] { objectType, typeof(ModelContainer) })
        foreach (var method in type.GetMethods().Where(m => m.DeclaringType == type
            && new[] { "Height", "Nameplate", "NamePlate", "Scale" }.Any(n => m.Name.Contains(n))))
            Console.WriteLine($"{type.Name}.{method}: {string.Join("; ", method.GetCustomAttributesData())}");
    Console.WriteLine("ModelChara: " + string.Join(", ", typeof(ModelChara).GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
    Console.WriteLine("Character attributes: " + string.Join("; ", typeof(Character).GetCustomAttributesData()));
    foreach (var method in typeof(FFXIVClientStructs.FFXIV.Client.Graphics.Scene.DrawObject).GetMethods()
        .Where(m => m.Name.Contains("AttachBone") || m.Name.Contains("TransformChanged")))
        Console.WriteLine($"DrawObject.{method}: {string.Join("; ", method.GetCustomAttributesData())}");
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    foreach (var n in probe.Excel.GetSheet<BNpcName>()!.Where(n => n.Singular.ToString() is "蜜蜂云" or "跳蜥幼体"))
        Console.WriteLine($"Battle NPC name: {n.RowId} {n.Singular}");
    return;
}

if (args.Length == 3 && args[1] == "--export-skill-data")
{
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var catalog = HuntModelSwapCN.Catalog.HuntCatalog.Load(probe.Excel.GetSheet<TerritoryType>()!,
        probe.Excel.GetSheet<BNpcName>()!, probe.Excel.GetSheet<PlaceName>()!);
    var models = probe.Excel.GetSheet<ModelChara>()!;
    var skills = HuntModelSwapCN.Catalog.SkillCatalog.Load(probe.Excel.GetSheet<Lumina.Excel.Sheets.Action>()!,
        probe.Excel.GetSheet<ActionTimeline>()!);
    var result = new {
        Hunts = catalog.Where(h => h.Rank == 3).Select(h => new { h.NameId, h.BaseId, h.ModelId, h.Name,
            h.Expansion, h.Scale, Family = HuntModelSwapCN.Catalog.SkillCatalog.ModelFamily(models.GetRow((uint)h.ModelId)) }),
        Actions = skills.Values,
        AllActions = probe.Excel.GetSheet<Lumina.Excel.Sheets.Action>()!.Where(a => !a.IsPlayerAction)
            .Select(a => new { ActionId = a.RowId, Name = a.Name.ToString(), End = a.AnimationEnd.RowId }),
        Resources = catalog.Where(h => h.Rank == 3).Select(h => new {
            h.NameId,
            Keys = skills.Values.Select(s => s.TimelineKey).Distinct().Where(k => {
                var family = HuntModelSwapCN.Catalog.SkillCatalog.ModelFamily(models.GetRow((uint)h.ModelId));
                return k.StartsWith($"mon_sp/{family}/", StringComparison.Ordinal) || k.StartsWith("mon_sp/[SKL_ID]/", StringComparison.Ordinal)
                    || k.StartsWith("mon_sp/common/", StringComparison.Ordinal) || k.StartsWith("magic/", StringComparison.Ordinal);
            }).Where(k => probe.FileExists($"chara/action/{k.Replace("[SKL_ID]", HuntModelSwapCN.Catalog.SkillCatalog.ModelFamily(models.GetRow((uint)h.ModelId)))}.tmb"))
        }),
    };
    File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(result));
    Console.WriteLine($"Exported {catalog.Count(h => h.Rank == 3)} S hunts and {skills.Count} playable action records.");
    Console.WriteLine(string.Join("\n", catalog.Where(h => h.Rank == 3).Select(h => $"{h.NameId}: {h.Name}")));
    return;
}

if (args.Length == 2 && args[1] == "--bufru-chain")
{
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var actions = probe.Excel.GetSheet<Lumina.Excel.Sheets.Action>()!;
    var timelines = probe.Excel.GetSheet<ActionTimeline>()!;
    foreach (var action in actions.Where(a => a.RowId is >= 27295 and <= 27320))
    {
        var start = timelines.GetRowOrDefault(action.AnimationStart.RowId);
        var end = timelines.GetRowOrDefault(action.AnimationEnd.RowId);
        Console.WriteLine($"{action.RowId}: {action.Name} cast={action.Cast100ms / 10f:F1}s start={action.AnimationStart.RowId}:{start?.Key} end={action.AnimationEnd.RowId}:{end?.Key} range={action.EffectRange} castType={action.CastType}");
    }
    return;
}

if (args.Length == 2 && args[1] == "--local-nameplate")
{
    Console.WriteLine($"Character.IsHostile writable={typeof(Character).GetProperty("IsHostile")?.CanWrite}");
    foreach (var field in new[] { "Flags", "Battalion", "Icon", "Level" })
        Console.WriteLine($"Character.{field} offset={System.Runtime.InteropServices.Marshal.OffsetOf<Character>(field)}");
    foreach (var methodName in new[] { "GetNamePlateColorType", "GetNamePlateColors" })
        Console.WriteLine($"{methodName}: {string.Join("; ", typeof(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject).GetMethod(methodName)!.GetCustomAttributesData().Select(a => a.ToString()))}");
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var bases = probe.Excel.GetSheet<BNpcBase>()!;
    foreach (var group in probe.Excel.GetSheet<NotoriousMonster>()!.Where(h => h.BNpcName.RowId > 0)
        .GroupBy(h => bases.GetRow(h.BNpcBase.RowId).Battalion.RowId))
        Console.WriteLine($"Hunt battalion={group.Key}, count={group.Count()}");
    foreach (var battalion in probe.Excel.GetSheet<Battalion>()!.Take(6))
        Console.WriteLine($"Battalion {battalion.RowId}: {string.Join(",", battalion.IsEnemyTo)}");
    return;
}

if (args.Length == 2 && args[1] == "--presentation")
{
    Console.WriteLine("Character.Icon offset=" + System.Runtime.InteropServices.Marshal.OffsetOf<Character>("Icon"));
    foreach (var type in new[] { typeof(BNpcBase), typeof(CharacterData), typeof(Character) })
    {
        Console.WriteLine(type.FullName);
        Console.WriteLine(string.Join("\n", type.GetProperties().Select(p => $"{p.Name}: {p.PropertyType.Name}")));
        Console.WriteLine(string.Join("\n", type.GetFields().Where(f => !f.IsStatic && (f.Name.Contains("Rank") || f.Name.Contains("Icon") || f.Name.Contains("Name"))).Select(f => $"{f.Name}: {f.FieldType.Name}")));
    }
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var presentationNames = probe.Excel.GetSheet<BNpcName>()!;
    foreach (var hunt in probe.Excel.GetSheet<NotoriousMonster>()!.Where(h => h.BNpcName.RowId > 0).Take(12))
        Console.WriteLine($"{presentationNames.GetRow(hunt.BNpcName.RowId).Singular}: rank={hunt.Rank} base={hunt.BNpcBase.RowId} npcRank={hunt.BNpcBase.Value.Rank}");
    return;
}

if (args.Length == 2 && args[1] == "--actions")
{
    foreach (var type in new[] { typeof(Lumina.Excel.Sheets.Action), typeof(ActionTimeline), typeof(TimelineContainer),
        typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.ActionTimelineSequencer),
        typeof(FFXIVClientStructs.FFXIV.Client.Game.Object.ClientObjectManager), typeof(CharacterSetupContainer) })
    {
        Console.WriteLine(type.FullName);
        Console.WriteLine(string.Join("\n", type.GetProperties().Select(p => $"{p.Name}: {p.PropertyType}")));
        Console.WriteLine(string.Join("\n", type.GetMethods().Where(m => m.DeclaringType == type && !m.IsSpecialName)));
    }
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var actions = probe.Excel.GetSheet<Lumina.Excel.Sheets.Action>()!;
    Console.WriteLine("Targetable flags: " + string.Join(", ", Enum.GetNames<FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectTargetableFlags>()));
    var timelines = probe.Excel.GetSheet<ActionTimeline>()!;
    foreach (var action in actions.Where(a => a.RowId >= 600 && !a.IsPlayerAction && a.AnimationEnd.RowId > 0).Take(35))
        if (timelines.TryGetRow(action.AnimationEnd.RowId, out var actionTimeline))
            Console.WriteLine($"{action.RowId}: {action.Name} end={action.AnimationEnd.RowId} key={actionTimeline.Key} slot={actionTimeline.Slot} loop={actionTimeline.IsLoop}");
    Console.WriteLine($"NPC actions={actions.Count(a => !a.IsPlayerAction && a.AnimationEnd.RowId > 0)}");
    Console.WriteLine("Key prefixes: " + string.Join(", ", actions.Where(a => !a.IsPlayerAction && a.AnimationEnd.RowId > 0)
        .Select(a => timelines.TryGetRow(a.AnimationEnd.RowId, out var t) ? t.Key.ToString().Split('/')[0] : "missing")
        .GroupBy(k => k).OrderByDescending(g => g.Count()).Take(15).Select(g => $"{g.Key}={g.Count()}")));
    foreach (var name in probe.Excel.GetSheet<BNpcName>()!.Where(n => n.Singular.ToString() == "布弗鲁"))
    {
        Console.WriteLine($"Bufru NameId={name.RowId}");
        foreach (var hunt in probe.Excel.GetSheet<NotoriousMonster>()!.Where(m => m.BNpcName.RowId == name.RowId))
            Console.WriteLine($"Bufru base={hunt.BNpcBase.RowId} model={hunt.BNpcBase.Value.ModelChara.RowId} modelResource={hunt.BNpcBase.Value.ModelChara.Value.Model}");
    }
    var skillNames = new[] { "巨魔碎颈臂", "五倍吸气", "五倍喷嚏", "上击", "腐烂孢子" };
    foreach (var action in actions.Where(a => skillNames.Contains(a.Name.ToString())))
    {
        var timeline = timelines.GetRowOrDefault(action.AnimationEnd.RowId);
        Console.WriteLine($"Bufru candidate {action.RowId}: {action.Name} start={action.AnimationStart.RowId} end={action.AnimationEnd.RowId} key={timeline?.Key} hit={action.ActionTimelineHit.RowId} cast={action.Cast100ms} player={action.IsPlayerAction}");
    }
    foreach (var timeline in timelines.Take(12))
        Console.WriteLine($"Timeline {timeline.RowId}: {timeline.Key} slot={timeline.Slot} loop={timeline.IsLoop}");
    return;
}

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

using Lumina;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

if (args.Length == 4 && args[1] == "--verify-game-ui-font")
{
    using var fontGame = new GameData(args[0]);
    var cnFont = HuntModelSwapCN.Game.ChineseGameFontData.Load(path => fontGame.GetFile(path)?.Data,
        path => fontGame.GetFile<Lumina.Data.Files.TexFile>(path) is { } tex ? new(tex.Header.Width, tex.Header.Height, tex.ImageData) : null);
    File.Copy(Path.Combine(args[3], "cimgui.dll"), Path.Combine(AppContext.BaseDirectory, "cimgui.dll"), true);
    List<object> results = [];
    foreach (var scale in new[] { 1f, 1.5f, 2f })
    {
        var context = Dalamud.Bindings.ImGui.ImGui.CreateContext();
        try
        {
            var atlas = Dalamud.Bindings.ImGui.ImGui.GetIO().Fonts;
            var font = atlas.AddFontDefault();
            font.FontSize = cnFont.LineHeight * scale;
            var plan = HuntModelSwapCN.Game.GameUiFont.Register(atlas, font, cnFont, scale);
            if (!atlas.Build()) throw new InvalidOperationException("Offline font atlas build failed.");
            HuntModelSwapCN.Game.GameUiFont.Populate(atlas, font, cnFont, plan);
            var verified = 0;
            unsafe
            {
                var buffers = new (nint Pointer, int Width)[atlas.Textures.Size];
                for (var i = 0; i < buffers.Length; i++)
                {
                    byte* pixels = null; var w = 0; var h = 0;
                    atlas.GetTexDataAsAlpha8(i, ref pixels, ref w, ref h);
                    buffers[i] = ((nint)pixels, w);
                }
                foreach (var (id, glyph, w, h) in plan.Rectangles)
                {
                    var rect = new Dalamud.Bindings.ImGui.ImFontAtlasCustomRectPtr(atlas.GetCustomRectByIndex(id));
                    var buffer = buffers[rect.TextureIndex];
                    for (var y = 0; y < h; y++)
                        for (var x = 0; x < w; x++)
                        {
                            var sx = Math.Min(glyph.Width - 1, (int)(x / scale));
                            var sy = Math.Min(glyph.Height - 1, (int)(y / scale));
                            var expected = sx < 0 || sy < 0 ? 0 : glyph.Alpha[sy * glyph.Width + sx];
                            if (((byte*)buffer.Pointer)[(rect.Y + y) * buffer.Width + rect.X + x] != expected)
                                throw new InvalidOperationException($"Font pixels differ for U+{(int)glyph.Character:X4}.");
                        }
                    if (font.FindGlyphNoFallback(glyph.Character) == null) throw new InvalidOperationException("Missing baked glyph.");
                    verified++;
                }
            }
            results.Add(new { Scale = scale, VerifiedGlyphs = verified, Textures = atlas.Textures.Size, font.FontSize, font.Ascent, font.Descent });
        }
        finally { Dalamud.Bindings.ImGui.ImGui.DestroyContext(context); }
    }
    var report = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(args[2], report);
    Console.WriteLine(report);
    return;
}

if (args.Length == 2 && args[1] == "--nameplate-level-font")
{
    foreach (var t in new[] { typeof(Dalamud.Bindings.ImGui.ImFontAtlasPtr), typeof(Dalamud.Bindings.ImGui.ImFontPtr), typeof(Dalamud.Bindings.ImGui.ImFontAtlasCustomRectPtr) })
    {
        foreach (var m in t.GetMethods().Where(m => m.DeclaringType == t && new[] { "CustomRect", "TexData", "AddFontDefault", "AddGlyph", "BuildLookup", "Kerning" }.Any(k => m.Name.Contains(k))))
            Console.WriteLine($"FONT API {t.Name}.{m}");
        Console.WriteLine($"FONT FIELDS {t.Name}: {string.Join(", ", t.GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}"))}");
    }
    foreach (var t in new[] { typeof(FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule),
        typeof(FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureTextModule),
        typeof(FFXIVClientStructs.FFXIV.Client.System.String.Utf8String) })
        foreach (var m in t.GetMethods().Where(m => m.DeclaringType == t && new[] { "Nameplate", "Number", "Normalize", "FormatAddonText" }.Any(k => m.Name.Contains(k, StringComparison.OrdinalIgnoreCase))))
            Console.WriteLine($"API {t.Name}.{m}: {string.Join(";", m.GetCustomAttributesData())}");
    foreach (var icon in Enum.GetValues<Dalamud.Game.Text.SeIconChar>().Where(v => v.ToString().StartsWith("Number") || v.ToString().StartsWith("Level")))
        Console.WriteLine($"Icon {icon}=U+{(int)icon:X4}");
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var uiFont = Dalamud.Interface.GameFonts.GameFontFamilyAndSize.Axis14;
    var uiFontAttribute = typeof(Dalamud.Interface.GameFonts.GameFontFamilyAndSize).GetField(uiFont.ToString())!
        .GetCustomAttributesData().Single(a => a.AttributeType.Name == "GameFontFamilyAndSizeAttribute");
    var uiFontPath = (string)uiFontAttribute.ConstructorArguments[0].Value!;
    Console.WriteLine($"CN SDK font {uiFont}: {uiFontPath}; {uiFontAttribute.ConstructorArguments[1].Value}");
    foreach (var fontPath in new[] { "common/font/AXIS_14.fdt", "common/font/chnaxis_140.fdt" })
    {
        var fontFile = probe.GetFile(fontPath);
        if (fontFile is null) { Console.WriteLine($"Font missing: {fontPath}"); continue; }
        var font = new Dalamud.Interface.GameFonts.FdtReader(fontFile.Data);
        var missing = "狩猎模型替换等级隐藏动作播放暂停继续旋转布弗鲁蜜蜂云跳蜥幼体俄菲翁尼厄斯0123456789".Distinct().Where(c => font.FindGlyphIndex(c) < 0).ToArray();
        Console.WriteLine($"Font {fontPath}: glyphs={font.Glyphs.Count}; missing={new string(missing)}");
        Console.WriteLine("Duplicate codepoints: " + string.Join(", ", font.Glyphs.GroupBy(g => g.CharInt).Where(g => g.Count() > 1).Take(10).Select(g => $"U+{g.Key:X4}={g.Count()}")));
        Console.WriteLine("Space glyphs: " + string.Join(", ", font.Glyphs.Where(g => g.CharInt == 32).Select(g => $"raw={g.CharUtf8:X8} advance={g.AdvanceWidth}")));
        Console.WriteLine("Metrics: " + string.Join(", ", font.FontHeader.GetType().GetFields().Select(f => $"{f.Name}={f.GetValue(font.FontHeader)}")));
    }
    foreach (var id in new uint[] { 35, 36, 242, 716, 866, 1093, 2034, 2035, 2039, 16599, 17740 })
    {
        var text = probe.Excel.GetSheet<Addon>().GetRow(id).Text;
        Console.WriteLine($"Raw Addon {id}: {Convert.ToHexString(text.Data.Span)}; {text.ToMacroString()}");
    }
    foreach (var row in probe.Excel.GetSheet<Addon>())
    {
        var text = row.Text.ToString();
        if (text.Length <= 160 && (text.Contains('级') || text.Any(c => c is >= '\uE060' and <= '\uE06C')))
            Console.WriteLine($"Addon {row.RowId}: {text}");
    }
    return;
}

if (args.Length == 2 && args[1] == "--auto-attacks")
{
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    Console.WriteLine("BNpcBase: " + string.Join(", ", typeof(BNpcBase).GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
    Console.WriteLine("ActionTimeline: " + string.Join(", ", typeof(ActionTimeline).GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
    foreach (var id in new uint[] { 112, 113, 114, 118, 121, 124, 127 })
    {
        var t = probe.Excel.GetSheet<ActionTimeline>().GetRow(id);
        var path = $"chara/action/{t.Key}.tmb";
        var file = probe.GetFile(path);
        var strings = file is null ? "missing" : string.Join(" | ", System.Text.RegularExpressions.Regex.Matches(
            System.Text.Encoding.ASCII.GetString(file.Data), "[a-zA-Z0-9_./\\[\\] -]{5,}").Select(m => m.Value));
        Console.WriteLine($"TMB {id} weaponTimeline={t.WeaponTimeline.RowId} residentPap={t.ResidentPap}: {strings}");
    }
    foreach (var leaf in new[] { "bt_common/resident/auto_attack1.pap", "bt_common/auto_attack1.pap", "bt_common/resident/attack1.pap" })
        Console.WriteLine($"PAP chara/monster/m0741/animation/a0001/{leaf}: {probe.FileExists($"chara/monster/m0741/animation/a0001/{leaf}")}");
    foreach (var t in probe.Excel.GetSheet<ActionTimeline>()!.Where(t => t.Key.ToString().Contains("attack", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"Timeline {t.RowId}: {t.Key} slot={t.Slot} loop={t.IsLoop}");
    foreach (var a in probe.Excel.GetSheet<Lumina.Excel.Sheets.Action>()!.Where(a => a.Name.ToString() is "攻击" or "自动攻击"))
        Console.WriteLine($"Action {a.RowId}: {a.Name} player={a.IsPlayerAction} start={a.AnimationStart.RowId} end={a.AnimationEnd.RowId} cast={a.Cast100ms}");
    return;
}

if (args.Length is 2 or 3 && args[1] == "--combat-api")
{
    foreach (var t in new[] { typeof(Character), typeof(CharacterData), typeof(TimelineContainer),
        typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.ActionTimelineSequencer) })
    {
        Console.WriteLine(t.FullName);
        foreach (var p in t.GetProperties().Where(p => p.Name.Contains("Combat") || p.Name.Contains("Weapon")))
            Console.WriteLine($"property {p.Name} get={p.CanRead} set={p.CanWrite}");
        foreach (var f in t.GetFields().Where(f => !f.IsStatic && new[] { "Flags", "State", "Timeline", "Target", "Override" }.Any(s => f.Name.Contains(s))))
            Console.WriteLine($"field {f.Name}:{f.FieldType.Name} offset={System.Runtime.InteropServices.Marshal.OffsetOf(t, f.Name):X}");
        foreach (var m in t.GetMethods().Where(m => m.DeclaringType == t && new[] { "Combat", "Weapon", "Timeline", "Target" }.Any(s => m.Name.Contains(s)) && !m.IsSpecialName))
            Console.WriteLine($"method {m}: {string.Join(";", m.GetCustomAttributesData())}");
    }
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    foreach (var t in probe.Excel.GetSheet<ActionTimeline>()!.Where(t => t.RowId < 100 || t.Key.ToString().Contains("battle") || t.Key.ToString().Contains("idle")).Take(130))
        Console.WriteLine($"Timeline {t.RowId}: {t.Key} slot={t.Slot} loop={t.IsLoop}");
    return;
}

if (args.Length == 2 && args[1] == "--target-visibility-api")
{
    foreach (var flag in Enum.GetValues<FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectTargetableFlags>())
        Console.WriteLine($"Targetable {flag} = 0x{(byte)flag:X2}");
    foreach (var flag in Enum.GetValues<FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags>())
        Console.WriteLine($"Visibility {flag} = 0x{(ulong)flag:X}");
    foreach (var t in new[] { typeof(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject), typeof(Character),
        typeof(BattleChara), typeof(FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group.SharedGroupLayoutInstance),
        typeof(FFXIVClientStructs.FFXIV.Client.LayoutEngine.ILayoutInstance) })
    {
        Console.WriteLine(t.FullName + ": " + string.Join(";", t.GetCustomAttributesData()));
        foreach (var m in t.GetMethods().Where(m => m.DeclaringType == t && new[] { "Target", "Draw", "Active", "Visible" }.Any(s => m.Name.Contains(s))
            && !m.IsSpecialName))
            Console.WriteLine($"{m}: {string.Join(";", m.GetCustomAttributesData())}");
    }
    return;
}

if (args.Length is 2 or 3 && args[1] == "--target-api")
{
    if (args.Length == 3)
        File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(
            new[] { "SetHardTarget", "SetSoftTarget" }.Select(name => {
                var method = typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem).GetMethod(name)!;
                return new { Name = name, Signature = method.GetCustomAttributesData()
                    .Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value };
            })));
    foreach (var t in new[] { typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.TargetSystem),
        typeof(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject) })
    {
        foreach (var m in t.GetMethods().Where(m => m.DeclaringType == t && (m.Name.Contains("Target") || m.Name.Contains("Highlight"))
            && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")))
            Console.WriteLine($"{m}: {string.Join(";", m.GetCustomAttributesData())}");
        foreach (var f in t.GetFields().Where(f => f.Name.Contains("Target") && !f.IsStatic))
            Console.WriteLine($"{f.Name}: {f.FieldType} offset={System.Runtime.InteropServices.Marshal.OffsetOf(t, f.Name):X}");
    }
    return;
}

if (args.Length == 2 && args[1] == "--striking-dummies")
{
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    foreach (var n in probe.Excel.GetSheet<BNpcName>()!.Where(n => n.Singular.ToString().Contains("木人")))
        Console.WriteLine($"Name {n.RowId}: {n.Singular}");
    Console.WriteLine("BNpcBase properties: " + string.Join(", ", typeof(BNpcBase).GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
    foreach (var id in new uint[] { 0x385, 0x2de0, 0x41cd })
    {
        var b = probe.Excel.GetSheet<BNpcBase>()!.GetRow(id);
        Console.WriteLine($"Dummy base={b.RowId} model={b.ModelChara.RowId} type={b.ModelChara.Value.Type} scale={b.Scale} rank={b.Rank} battalion={b.Battalion.RowId} behavior={b.Behavior.RowId}");
    }
    return;
}

if (args.Length == 2 && args[1] == "--redraw-api")
{
    foreach (var t in new[] { typeof(Character), typeof(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject),
        typeof(FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase),
        typeof(FFXIVClientStructs.FFXIV.Client.Game.Character.ModelContainer),
        typeof(FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group.SharedGroupLayoutInstance) })
    {
        Console.WriteLine(t.FullName + " attrs=" + string.Join(";", t.GetCustomAttributesData()));
        foreach (var f in t.GetFields().Where(f => !f.IsStatic && new[] { "Model", "Draw", "Layout", "Flags", "ObjectKind", "SubKind" }.Any(x => f.Name.Contains(x))))
            Console.WriteLine($" field {f.Name}:{f.FieldType.Name} offset={System.Runtime.InteropServices.Marshal.OffsetOf(t, f.Name):X}");
        foreach (var m in t.GetMethods().Where(m => m.DeclaringType == t && new[] { "Draw", "Active", "Model", "Visible" }.Any(x => m.Name.Contains(x)) && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")))
            Console.WriteLine($" method {m}: {string.Join(";", m.GetCustomAttributesData())}");
    }
    return;
}

if (args.Length == 2 && args[1] == "--special-hunts")
{
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    string[] wanted = ["克尔的侍从", "克尔", "得到宽恕的流言", "得到宽恕的叛乱", "水晶化身", "水晶化身之王"];
    var specialNames = probe.Excel.GetSheet<BNpcName>()!;
    var marks = probe.Excel.GetSheet<NotoriousMonster>()!.Where(h => h.BNpcName.RowId > 0 && h.BNpcBase.RowId > 0
        && wanted.Contains(specialNames.GetRow(h.BNpcName.RowId).Singular.ToString()));
    foreach (var h in marks.DistinctBy(h => (h.BNpcName.RowId, h.BNpcBase.RowId)))
        Console.WriteLine($"{specialNames.GetRow(h.BNpcName.RowId).Singular}: name={h.BNpcName.RowId} base={h.BNpcBase.RowId} huntRank={h.Rank} model={h.BNpcBase.Value.ModelChara.RowId} scale={h.BNpcBase.Value.Scale} icon={h.BNpcBase.Value.Rank} battalion={h.BNpcBase.Value.Battalion.RowId}");
    return;
}

if (args.Length == 3 && args[1] == "--export-name-icons")
{
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    Directory.CreateDirectory(args[2]);
    foreach (var id in new[] { 61704, 61710 })
    {
        var path = $"ui/icon/061000/{id:D6}.tex";
        var tex = probe.GetFile<Lumina.Data.Files.TexFile>(path) ?? throw new Exception($"Missing {path}");
        Console.WriteLine($"{path}: {tex.Header.Width}x{tex.Header.Height}, {tex.ImageData.Length} bytes");
        File.WriteAllBytes(Path.Combine(args[2], $"{id}.bgra"), tex.ImageData);
    }
    return;
}

if (args.Length == 2 && args[1] == "--aggro-icons")
{
    Console.WriteLine("Behavior: " + string.Join(", ", typeof(Behavior).GetProperties().Select(p => $"{p.Name}:{p.PropertyType.Name}")));
    foreach (var type in new[] { typeof(Character), typeof(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject) })
        foreach (var m in type.GetMethods().Where(m => m.DeclaringType == type && (m.Name.Contains("Icon") || m.Name.Contains("NamePlate"))))
            Console.WriteLine($"{m}: {string.Join("; ", m.GetCustomAttributesData())}");
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var n = probe.Excel.GetSheet<BNpcName>()!;
    var marks = probe.Excel.GetSheet<NotoriousMonster>()!.Where(h => h.BNpcName.RowId != 0 && h.BNpcBase.RowId != 0).ToArray();
    foreach (var g in marks.GroupBy(h => h.BNpcBase.Value.Behavior.RowId))
        Console.WriteLine($"Behavior {g.Key}: count={g.Count()}");
    foreach (var h in marks.Where(h => n.GetRow(h.BNpcName.RowId).Singular.ToString().Contains("第十三") || h.Rank == 3)
        .OrderByDescending(h => n.GetRow(h.BNpcName.RowId).Singular.ToString().Contains("第十三")).Take(12))
    {
        var b = h.BNpcBase.Value;
        Console.WriteLine($"{n.GetRow(h.BNpcName.RowId).Singular} base={b.RowId} behavior={b.Behavior.RowId} rank={b.Rank}");
        if (b.Behavior.ValueNullable is { } behavior)
            foreach (var p in typeof(Behavior).GetProperties().Where(p => p.Name is not ("ExcelPage" or "RowOffset")))
                Console.WriteLine($" {p.Name}: {p.GetValue(behavior)}");
    }
    return;
}

if (args.Length is 2 or 3 && args[1] == "--dialogue")
{
    foreach (var type in new[] { typeof(NpcYell), typeof(FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloon),
        typeof(Dalamud.Plugin.Services.IChatGui.OnChatMessageDelegate), typeof(Dalamud.Game.Chat.IChatMessage) })
    {
        Console.WriteLine(type.FullName);
        foreach (var p in type.GetProperties()) Console.WriteLine($"  {p.Name}: {p.PropertyType}");
        foreach (var f in type.GetFields().Where(f => !f.IsStatic)) Console.WriteLine($"  {f.Name}: {f.FieldType}");
        foreach (var m in type.GetMethods().Where(m => m.DeclaringType == type && !m.IsSpecialName))
            Console.WriteLine($"  {m}: {string.Join("; ", m.GetCustomAttributesData())}");
    }
    using var probe = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Lumina.Data.Language.ChineseSimplified });
    var rows = probe.Excel.GetSheet<NpcYell>()!;
    var logKind = typeof(Dalamud.Game.Chat.IChatMessage).GetProperty("LogKind")!.PropertyType;
    Console.WriteLine("LogKind " + logKind + " " + string.Join(",", Enum.GetNames(logKind).Where(n => n.Contains("Npc", StringComparison.OrdinalIgnoreCase))));
    var hunts = HuntModelSwapCN.Catalog.HuntCatalog.Load(probe.Excel.GetSheet<TerritoryType>()!, probe.Excel.GetSheet<BNpcName>()!, probe.Excel.GetSheet<PlaceName>()!);
    var ids = hunts.Where(h => h.Rank == 3).Select(h => h.NameId).ToHashSet();
    var matched = rows.Where(r => ids.Contains(r.Name.RowId)).ToArray();
    if (args.Length == 3)
        File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(new {
            Hunts = hunts.Where(h => h.Rank == 3).Select(h => new { h.NameId, h.Name, h.ModelId }),
            Rows = rows.Select(r => new { r.RowId, Text = r.Text.ExtractText(), Seconds = r.BalloonTime }),
            Signatures = new[] { "OpenBalloon", "CloseBalloon" }.Select(name => new {
                Name = name, Signature = typeof(FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloon).GetMethods()
                    .First(m => m.Name == name && m.GetCustomAttributesData().Any(a => a.AttributeType.Name == "MemberFunctionAttribute"))
                    .GetCustomAttributesData().Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value,
            }),
        }));
    Console.WriteLine($"S DIALOGUES {matched.Length}");
    foreach (var g in matched.GroupBy(r => r.Name.RowId))
        Console.WriteLine($"{g.Key} {hunts.First(h => h.NameId == g.Key).Name}: {g.Count()}");
    foreach (var row in rows.Where(r => r.RowId is >= 16874 and <= 16906))
    {
        Console.WriteLine($"{row.RowId} name={row.Name.RowId} time={row.BalloonTime} output={row.OutputType} unk6={row.Unknown6} {row.Text}");
    }
    return;
}

if (args.Length is 2 or 3 && args[1] == "--animation-pause")
{
    foreach (var type in new[] { typeof(TimelineContainer), typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.ActionTimelineSequencer) })
    {
        Console.WriteLine(type.FullName);
        foreach (var f in type.GetFields().Where(f => !f.IsStatic && (f.Name.Contains("Speed") || f.Name.Contains("Parent") || f.Name.Contains("Owner"))))
            Console.WriteLine($"{f.Name}: {f.FieldType} offset={System.Runtime.InteropServices.Marshal.OffsetOf(type, f.Name):X}");
        foreach (var m in type.GetMethods().Where(m => m.DeclaringType == type && m.Name.Contains("Speed")))
            Console.WriteLine($"{m}: {string.Join("; ", m.GetCustomAttributesData())}");
        Console.WriteLine("Addresses: " + string.Join(",", type.GetNestedType("Addresses")?.GetFields().Select(f => f.Name) ?? []));
    }
    if (args.Length == 3)
    {
        var methods = new[] { typeof(TimelineContainer).GetMethod("CalculateAndApplyOverallSpeed")!,
            typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.ActionTimelineSequencer).GetMethod("SetSlotSpeed")! };
        File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(methods.Select(m => new
        {
            m.Name,
            Signature = m.GetCustomAttributesData().Single(a => a.AttributeType.Name == "MemberFunctionAttribute").ConstructorArguments[0].Value,
        })));
    }
    return;
}

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
        Hunts = catalog.Select(h => new { h.NameId, h.BaseId, h.ModelId, h.Name,
            h.Expansion, h.Scale, h.Rank, h.IsSpecial, h.ModelType, h.MainHand, h.OffHand, Family = HuntModelSwapCN.Catalog.SkillCatalog.ModelFamily(models.GetRow((uint)h.ModelId)) }),
        Actions = skills.Values,
        AllActions = probe.Excel.GetSheet<Lumina.Excel.Sheets.Action>()!.Where(a => !a.IsPlayerAction)
            .Select(a => new { ActionId = a.RowId, Name = a.Name.ToString(), End = a.AnimationEnd.RowId }),
        Resources = catalog.Select(h => new {
            h.NameId,
            Keys = skills.Values.Select(s => s.TimelineKey).Distinct().Where(k => {
                var family = HuntModelSwapCN.Catalog.SkillCatalog.ModelFamily(models.GetRow((uint)h.ModelId));
                return k.StartsWith($"mon_sp/{family}/", StringComparison.Ordinal) || k.StartsWith("mon_sp/[SKL_ID]/", StringComparison.Ordinal)
                    || k.StartsWith("mon_sp/common/", StringComparison.Ordinal) || k.StartsWith("magic/", StringComparison.Ordinal)
                    || k.StartsWith("mon_sp/gimmick/", StringComparison.Ordinal)
                    || (h.NameId == 4361 && k.StartsWith("ws/bt_swd_sld/", StringComparison.Ordinal))
                    || (h.NameId == 2926 && k.StartsWith("ws/bt_2sp_emp/", StringComparison.Ordinal));
            }).Where(k => probe.FileExists($"chara/action/{k.Replace("[SKL_ID]", HuntModelSwapCN.Catalog.SkillCatalog.ModelFamily(models.GetRow((uint)h.ModelId)))}.tmb"))
        }),
    };
    File.WriteAllText(args[2], System.Text.Json.JsonSerializer.Serialize(result));
    Console.WriteLine($"Exported {catalog.Count} hunts and {skills.Count} playable action records.");
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

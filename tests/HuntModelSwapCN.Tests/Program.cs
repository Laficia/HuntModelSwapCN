using HuntModelSwapCN.Core;
using HuntModelSwapCN.Catalog;
using Lumina;
using Lumina.Data;
using Lumina.Excel.Sheets;
using HuntModelSwapCN.Game;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

var tests = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    tests++;
}
var actor = new ActorIdentity(12, 100, 22, 33, 0x1000);
var other = new ActorIdentity(14, 101, 23, 34, 0x2000);
var original = State(12, 1.25f);
var access = new FakeAccess();
access.Actors.Add(actor, original);
var session = new SwapSession(access);
session.Apply(actor, "小怪", "S怪", State(235, 3));
Check(access.Hidden.Contains(actor), "Apply must wait for a subsequent tick to finish redraw.");
session.Tick();
Check(!access.Hidden.Contains(actor), "A pending redraw must be enabled on the next tick.");
access.Actors[actor] = access.Actors[actor] with { NameBytes = DisplayName.Encode("服务器刷新后的原名"), Level = 12,
    ModelScale = 0.5f, ObjectScale = 0.5f };
session.Tick();
Check(access.Actors[actor].NameBytes.SequenceEqual(DisplayName.Encode("怪物235")) && access.Actors[actor].Level == 7,
    "A server display update must not permanently remove the replacement name or level.");
Check(access.Actors[actor].ModelScale == 3 && access.Actors[actor].ObjectScale == 3,
    "Source scale updates must not replace the selected creature's true size.");
session.Apply(actor, "第一次替换后的名字", "A怪", State(19, 2));
Check(session.Entries.Single().OriginalName == "小怪", "Repeated replacements must retain the real original display name.");
session.Tick();
session.Restore(actor);
session.Tick();
Check(access.Actors[actor] == original, "Repeated replacements must retain the first complete snapshot.");
Check(session.Entries.Count == 0, "Restored actors must no longer be tracked.");
session.Tick();
Check(access.Actors[actor] == original, "A later tick must not reapply a name, level or scale after restore.");

session.Apply(actor, "小怪", "S怪", State(235, 3));
session.Restore(actor);
session.Tick();
Check(access.Actors[actor] == original && !access.Hidden.Contains(actor), "Restore during redraw must restore and enable.");

session.Apply(actor, "小怪", "S怪", State(235, 3));
access.Actors.Remove(actor);
access.Hidden.Remove(actor);
var reused = actor with { ObjectId = 999, NameId = 88 };
access.Actors.Add(reused, original);
session.Tick();
session.RestoreAll();
Check(access.Actors[reused] == original, "An index/address reused by another actor must never receive the old snapshot.");
Check(!access.Writes.Any(w => w.Actor == reused), "No native write may target a reused slot.");
Check(session.Entries.Count == 0, "Despawned actors must be forgotten.");

access.Actors.Add(actor, original);
access.Actors.Add(other, State(13, 1));
session.Apply(actor, "小怪", "S怪", State(235, 3));
session.Apply(other, "小怪2", "A怪", State(19, 2));
session.RestoreAll();
session.Tick();
Check(access.Actors[actor] == original && access.Actors[other].ModelId == 13, "Restore-all must restore every actor.");

session.Apply(actor, "小怪", "S怪", State(235, 3));
session.Apply(other, "小怪2", "A怪", State(19, 2));
access.FailBeginFor = actor;
try { session.Shutdown(); throw new Exception("Expected an aggregate restore error."); }
catch (AggregateException) { }
Check(!access.Hidden.Contains(actor) && !access.Hidden.Contains(other), "Unload must enable pending actors even after a restore failure.");
Check(access.Actors[other].ModelId == 13, "A failing restore must not prevent restoring other actors.");
Check(session.Entries.Count == 0, "Unload must release all managed leases.");

// Use a synthetic native struct, never a running process or native game function.
unsafe
{
    NativeCharacter native = default;
    native.NameId = 987;
    native.GameObject.BaseId = 654;
    native.Health = 42;
    native.MaxHealth = 100;
    native.GameObject.HitboxRadius = 2.5f;
    native.GameObject.Scale = 1.25f;
    native.ModelScale = 1.25f;
    native.Level = 33;
    DisplayName.Encode("原来的小怪").CopyTo(native.GameObject.Name);
    native.GameObject.UpdateFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Unk2;
    FFXIVClientStructs.FFXIV.Client.Graphics.Scene.DrawObject draw = default;
    draw.Object.Scale = new System.Numerics.Vector3(0.7f, 1.2f, 1.4f);
    native.GameObject.DrawObject = &draw;
    var originalNative = NativeAppearanceCodec.Capture(&native);
    native.GameObject.Position = new System.Numerics.Vector3(12, 34, 56);
    var initialPosition = native.GameObject.Position;
    var source = new VisualState(235, -1, 3.5f, Enumerable.Range(0, 26).Select(i => (byte)i).ToArray(),
        Enumerable.Range(1, 10).Select(i => (ulong)(uint)i | ((ulong)(uint)(i + 1) << 32)).ToArray(),
        [0x1020304050607080UL, 0x8070605040302010UL, 0], 0x32, 0x14,
        DisplayName.Encode("雷德罗巨蛇"), 50, 3.5f, new System.Numerics.Vector3(3.5f));
    NativeAppearanceCodec.Write(&native, source);
    var captured = NativeAppearanceCodec.Capture(&native);
    Check(captured.ModelId == source.ModelId && captured.SecondaryModelId == -1 && captured.ModelScale == 3.5f,
        "Native model and scale fields must round-trip.");
    Check(captured.Customize.SequenceEqual(source.Customize) && captured.Equipment.SequenceEqual(source.Equipment)
        && captured.Weapons.SequenceEqual(source.Weapons) && captured.Flags1 == source.Flags1 && captured.Flags2 == source.Flags2,
        "Customization, dyes, equipment, weapons and draw flags must round-trip without truncation.");
    Check(native.NameId == 987 && native.GameObject.BaseId == 654 && native.Health == 42 && native.MaxHealth == 100
        && native.GameObject.HitboxRadius == 2.5f
        && native.GameObject.Position == initialPosition, "Appearance writes must not overwrite identity, HP, hitbox or position.");
    Check(captured.NameBytes.SequenceEqual(source.NameBytes) && captured.Level == 50
        && native.GameObject.NameString == "雷德罗巨蛇", "Names must be encoded as complete, terminated UTF-8 and level must match.");
    Check(native.GameObject.Scale == 3.5f && captured.ObjectScale == 3.5f,
        "Object scale must follow the target's original scale, rather than the source creature's size.");
    Check((System.Numerics.Vector3)draw.Object.Scale == new System.Numerics.Vector3(3.5f), "The actual draw transform must use the target's true scale.");
    Check(native.GameObject.UpdateFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Name)
        && native.GameObject.UpdateFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Unk2),
        "Renaming must dirty the name while preserving other queued update flags.");
    Check(!NativeAppearanceCodec.WritePresentation(&native, source), "Unchanged name and level must not request a nameplate redraw.");
    NativeAppearanceCodec.Write(&native, originalNative);
    Check(native.GameObject.NameString == "原来的小怪" && native.Level == 33,
        "Restore must round-trip the original Unicode name and level.");
    Check(native.ModelScale == 1.25f && native.GameObject.Scale == 1.25f
        && (System.Numerics.Vector3)draw.Object.Scale == new System.Numerics.Vector3(0.7f, 1.2f, 1.4f),
        "Restore must round-trip all original scale components.");
    try { NativeAppearanceCodec.Write(&native, source with { Customize = new byte[25] }); throw new Exception("Invalid appearance accepted."); }
    catch (InvalidOperationException) { tests++; }
}
try { DisplayName.Encode(new string('蛇', 22)); throw new Exception("An overlong UTF-8 name was accepted."); }
catch (InvalidOperationException) { tests++; }

if (args.Length == 1)
{
    using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.ChineseSimplified });
    var catalog = HuntCatalog.Load(game.Excel.GetSheet<TerritoryType>(Language.ChineseSimplified),
        game.Excel.GetSheet<BNpcName>(Language.ChineseSimplified), game.Excel.GetSheet<PlaceName>(Language.ChineseSimplified));
    Console.WriteLine("Catalog: " + string.Join(", ", catalog.GroupBy(h => h.RankName).Select(g => $"{g.Key}={g.Count()}")));
    Check(catalog.Count == 201, "The installed 2.0-7.0 client must supply 201 regular hunts.");
    Check(catalog.Count(h => h.Rank == 3) == 47, "S list must exclude SS bosses.");
    Check(catalog.Count(h => h.Rank == 1) == 77 && catalog.Count(h => h.Rank == 2) == 77, "B/A lists must exclude SS minions.");
    Check(catalog.Single(h => h.NameId == 2953).ModelId == 235, "Name ID must resolve to a ModelChara ID, not be used as the model.");
    Check(catalog.All(h => h.ModelId >= 0 && h.BaseId > 0 && float.IsFinite(h.Scale) && h.Scale > 0
        && h.Customize.Length == 26 && h.Equipment.Length == 10), "Every hunt must have complete appearance data.");
    Check(catalog.Single(h => h.NameId == 13360) is { ModelType: 2 } profile && profile.Equipment.Any(e => e != 0),
        "Demihuman S hunts must include the NPC's body equipment.");
    Check(catalog.All(h => h.Level == (h.Expansion + 5) * 10 && h.Level is >= 50 and <= 100),
        "Every regular 2.0-7.0 hunt must have its matching numeric level, independently of S/A/B rank.");
    Check(catalog.All(h => DisplayName.Encode(h.Name).Length == DisplayName.Capacity),
        "Every catalog name must fit in a complete, null-terminated native UTF-8 name buffer.");
    foreach (var hunt in catalog)
    {
        if (hunt.ModelId == 0)
        {
            Check(hunt.ModelType == 1 && hunt.Customize[0] != 0 && hunt.Equipment.Any(e => e != 0),
                $"Human model 0 requires customization and equipment: {hunt.Name}");
            continue;
        }
        var model = game.Excel.GetSheet<ModelChara>().GetRow((uint)hunt.ModelId);
        var (folder, prefix) = hunt.ModelType switch { 1 => ("human", "c"), 2 => ("demihuman", "d"), _ => ("monster", "m") };
        var path = $"chara/{folder}/{prefix}{model.Model:D4}/skeleton/base/b0001/skl_{prefix}{model.Model:D4}b0001.sklb";
        Check(game.FileExists(path), $"Missing skeleton for {hunt.Name}: {path}");
    }
}
Console.WriteLine($"PASS: {tests} checks.");

static VisualState State(int id, float scale) => new(id, -1, scale, new byte[26], new ulong[10], new ulong[3], 0, 0,
    DisplayName.Encode($"怪物{id}"), 7, scale, new System.Numerics.Vector3(scale));
sealed class FakeAccess : IActorAccess
{
    public Dictionary<ActorIdentity, VisualState> Actors { get; } = new();
    public HashSet<ActorIdentity> Hidden { get; } = new();
    public List<(ActorIdentity Actor, VisualState State)> Writes { get; } = new();
    public ActorIdentity? FailBeginFor { get; set; }
    public bool Exists(ActorIdentity actor) => Actors.ContainsKey(actor);
    public VisualState Read(ActorIdentity actor) => Actors[actor];
    public void BeginRedraw(ActorIdentity actor, VisualState state)
    {
        if (FailBeginFor == actor) throw new InvalidOperationException("Simulated native wrapper failure.");
        Actors[actor] = state;
        Writes.Add((actor, state));
        Hidden.Add(actor);
    }
    public void FinishRedraw(ActorIdentity actor) => Hidden.Remove(actor);
    public void RefreshPresentation(ActorIdentity actor, VisualState state)
        => Actors[actor] = Actors[actor] with { NameBytes = state.NameBytes, Level = state.Level,
            ModelScale = state.ModelScale, ObjectScale = state.ObjectScale, DrawScale = state.DrawScale };
}

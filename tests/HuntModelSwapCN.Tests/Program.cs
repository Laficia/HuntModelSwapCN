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
Check(session.NameFor(actor) == "S怪" && session.NameFor(actor with { ObjectId = 999 }) is null,
    "The rendered name override must select the replacement and reject recycled identities.");
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
Check(session.NameFor(actor) == "小怪", "Restoring must immediately stop rendering the replacement name.");
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
    native.Icon = 6;
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
        DisplayName.Encode("雷德罗巨蛇"), 50, 3.5f, new System.Numerics.Vector3(3.5f), 1);
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
    Check(native.Icon == 1 && captured.NameplateIcon == 1, "The target NPC's nameplate icon category must replace the source category.");
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
    Check(native.Icon == 6, "Restore must restore the original nameplate icon category.");
    Check(native.ModelScale == 1.25f && native.GameObject.Scale == 1.25f
        && (System.Numerics.Vector3)draw.Object.Scale == new System.Numerics.Vector3(0.7f, 1.2f, 1.4f),
        "Restore must round-trip all original scale components.");
    try { NativeAppearanceCodec.Write(&native, source with { Customize = new byte[25] }); throw new Exception("Invalid appearance accepted."); }
    catch (InvalidOperationException) { tests++; }
}
try { DisplayName.Encode(new string('蛇', 22)); throw new Exception("An overlong UTF-8 name was accepted."); }
catch (InvalidOperationException) { tests++; }

// Preview must never reach native playback for a stale, untracked or redrawing target.
var animationAccess = new FakeAnimationAccess();
var previewAccess = new FakeAccess();
previewAccess.Actors.Add(actor, original);
var previewSession = new SwapSession(previewAccess);
var preview = new ActionPreview(previewSession, animationAccess);
var testSkill = new SkillAnimation(27316, "巨魔碎颈臂", 3205, "mon_sp/[SKL_ID]/mon_sp005", 1);
void RefusePreview(ActorIdentity target, long time, string message)
{
    var calls = animationAccess.Calls.Count;
    try { preview.Play(target, testSkill, time); throw new Exception(message); }
    catch (InvalidOperationException) { Check(animationAccess.Calls.Count == calls, message); }
}
RefusePreview(actor, 0, "Untracked actors must not receive animation playback.");
previewSession.Apply(actor, "小怪", "布弗鲁", State(3315, 3));
RefusePreview(actor, 0, "Pending redraws must not receive animation playback.");
previewSession.Tick();
preview.Play(actor, testSkill, 0);
Check(animationAccess.Calls.Single() == (actor, 3315, (ushort)3205),
    "Playback must use the replaced actor, applied model, and AnimationEnd ID rather than Action ID.");
RefusePreview(actor, 749, "Rapid playback must not flood the native scheduler.");
preview.Play(actor, testSkill, 750);
Check(animationAccess.Calls.Count == 2, "Playback may resume at the documented interval.");
previewSession.Restore(actor);
RefusePreview(actor, 1500, "Restoring actors must not receive playback.");
previewSession.Tick();
RefusePreview(actor, 1500, "Restored actors must no longer receive playback.");
previewSession.Apply(actor, "小怪", "布弗鲁", State(3315, 3));
previewSession.Tick();
RefusePreview(reused, 1500, "A recycled object slot must not inherit permission to play animations.");
preview.Clear();
preview.Play(actor, testSkill, 751);
Check(animationAccess.Calls.Count == 3, "Clearing a scene must reset the playback cooldown.");
previewSession.ForgetDespawned();
RefusePreview(actor, 1501, "Scene clearing must discard all preview targets.");
Check(PreviewCommand.Parse(" PLAY  27316 ") == 27316 && PreviewCommand.IsPlay("play")
    && !PreviewCommand.IsPlay("player 27316"), "Command parsing must recognize the play subcommand exactly.");
foreach (var command in new[] { "play", "play 0", "play -1", "play 1.2", "play 4294967296", "play 27316 extra", "play +27316" })
{
    try { PreviewCommand.Parse(command); throw new Exception($"Invalid command accepted: {command}"); }
    catch (InvalidOperationException) { tests++; }
}
Check(testSkill.Matches("27316") && testSkill.Matches("碎颈") && testSkill.Family == "[SKL_ID]",
    "Search and dynamic skeleton resources must retain their meaning.");

var localAccess = new FakeAccess();
var localSession = new SwapSession(localAccess);
var localFactory = new FakeLocalAccess(localAccess);
var localPreview = new LocalPreview(localSession, localFactory);
var sampleHunt = new HuntAppearance(10617, 13834, 3315, 3, 4, 961, "布弗鲁", "厄尔庇斯", 3, 3,
    new byte[26], new ulong[10], 0, 0, false);
var firstLocal = localPreview.Spawn(sampleHunt, 1, 8);
Check(localPreview.Owns(firstLocal) && localSession.Entries.Count == 1 && firstLocal.DataId == 0 && firstLocal.NameId == 0,
    "A local preview must be tracked independently and have no live hunt identity.");
var changedLocal = localPreview.PreserveOwnership(firstLocal, State(235, 2));
Check(LocalPreviewName.Matches(changedLocal.NameBytes, localPreview.Actor!.Token),
    "Replacing an owned local actor must retain its ownership marker.");
var secondLocal = localPreview.Spawn(sampleHunt, 1, 8);
Check(localFactory.Deleted.Count == 1 && localSession.Entries.Single().Actor == secondLocal && !localAccess.Actors.ContainsKey(firstLocal),
    "Spawning again must remove the old owned actor and retain only one preview.");
localFactory.FailDelete = true;
try { localPreview.Clear(); throw new Exception("Expected delete failure."); } catch (InvalidOperationException) { }
Check(localPreview.Actor?.Actor == secondLocal && localSession.Entries.Count == 1,
    "A failed delete must retain ownership so cleanup can be retried.");
localFactory.FailDelete = false;
localPreview.Clear();
Check(localFactory.Deleted.Count == 2 && localPreview.Actor is null && localSession.Entries.Count == 0,
    "Removing a preview must clear native ownership and its swap session.");
var recycledLocal = localPreview.Spawn(sampleHunt, 1, 8);
localAccess.Actors[recycledLocal] = State(12, 1); // Same slot/address/IDs, but a different owner.
localPreview.Tick();
localPreview.Clear();
Check(localFactory.Deleted.Count == 2 && localAccess.Actors[recycledLocal].ModelId == 12 && localSession.Entries.Count == 0,
    "Losing the native ownership marker must never delete a recycled local actor.");
localPreview.Spawn(sampleHunt, 1, 8);
localPreview.ForgetScene();
Check(localFactory.Deleted.Count == 2 && localPreview.Actor is null && localSession.Entries.Count == 0,
    "Forgetting an old scene must never delete its old slot in the next scene.");
localFactory.FailDraw = true;
try { localPreview.Spawn(sampleHunt, 1, 8); throw new Exception("Expected draw failure."); } catch (InvalidOperationException) { }
Check(localPreview.Actor is null && localSession.Entries.Count == 0 && localFactory.Deleted.Count == 3,
    "A partial preview setup failure must clean up the native actor and the session.");
var forward = SpawnPlacement.InFront(new(1, 2, 3), 0, 8);
var right = SpawnPlacement.InFront(new(1, 2, 3), MathF.PI / 2, 8);
Check(forward == new System.Numerics.Vector3(1, 2, 11) && MathF.Abs(right.X - 9) < 0.001f && right.Y == 2,
    "Local placement must use player facing and preserve current height.");
foreach (var distance in new[] { 0f, 31f, float.NaN })
    try { SpawnPlacement.InFront(default, 0, distance); throw new Exception("Invalid placement accepted."); }
    catch (InvalidOperationException) { tests++; }
var markedName = LocalPreviewName.Stamp(DisplayName.Encode("布弗鲁"), 12345);
Check(LocalPreviewName.Matches(markedName, 12345) && !LocalPreviewName.Matches(markedName, 12346)
    && System.Text.Encoding.UTF8.GetString(markedName.AsSpan(0, Array.IndexOf(markedName, (byte)0))) == "布弗鲁",
    "The ownership marker must not alter the visible C-string name.");

var visibilityAccess = new FakeVisibilityAccess();
visibilityAccess.Flags.Add(actor, false);
visibilityAccess.Flags.Add(other, false);
visibilityAccess.Flags.Add(reused, true); // Already hidden before this plugin acts.
var visibility = new ModelVisibility(visibilityAccess);
var candidates = new[] { new VisibilityCandidate(actor, true, false), new VisibilityCandidate(other, false, true),
    new VisibilityCandidate(reused, false, true) };
var unprotected = new HashSet<ActorIdentity>();
visibility.SetOptions(true, false);
visibility.Tick(candidates, unprotected);
Check(visibilityAccess.Flags[actor] && !visibilityAccess.Flags[other] && visibility.Count == 1,
    "The self-only switch must not hide unrelated monsters.");
visibility.SetOptions(true, true);
visibility.Tick(candidates, new HashSet<ActorIdentity> { other });
Check(!visibilityAccess.Flags[other] && visibilityAccess.Flags[reused] && visibility.Count == 2,
    "Generated or replaced preview actors must remain visible while other monsters are hidden.");
visibilityAccess.Flags[actor] = false;
visibility.Tick(candidates, unprotected);
Check(visibilityAccess.Flags[actor] && visibilityAccess.Flags[other],
    "Active hiding must survive game refreshes and include newly eligible actors.");
visibility.Tick(candidates, new HashSet<ActorIdentity> { other });
Check(!visibilityAccess.Flags[other], "An actor promoted to a preview must be released from the hide set.");
visibility.Reset();
Check(!visibilityAccess.Flags[actor] && !visibilityAccess.Flags[other] && visibilityAccess.Flags[reused] && visibility.Count == 0
    && !visibility.Active, "Reset must preserve objects that were hidden before the plugin and disable both switches.");
visibility.SetOptions(true, true);
visibility.Tick(candidates, unprotected);
visibilityAccess.Flags.Remove(actor);
var newOwner = actor with { ObjectId = 777, NameId = 0 };
visibilityAccess.Flags.Add(newOwner, true);
visibility.Reset();
Check(visibilityAccess.Flags[newOwner], "Restoring visibility must never change a reused object's visibility.");
visibility.SetOptions(false, true);
visibility.Tick(candidates, unprotected);
visibilityAccess.FailShowFor = other;
try { visibility.Reset(); throw new Exception("Expected visibility restore failure."); } catch (AggregateException) { }
Check(visibility.Count == 1 && !visibility.Active, "A failed restore must retain only the failing actor for retry.");
visibilityAccess.FailShowFor = null;
visibility.Reset();
Check(visibility.Count == 0 && !visibilityAccess.Flags[other], "Visibility cleanup must be retryable.");
visibility.SetOptions(false, true);
visibility.Tick(candidates, unprotected);
var writeCount = visibilityAccess.Writes;
visibility.ForgetScene();
Check(!visibility.Active && visibility.Count == 0 && visibilityAccess.Writes == writeCount,
    "Discarding old-scene visibility leases must not write into a new scene.");
unsafe
{
    FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject native = default;
    native.RenderFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags.Nameplate | (FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags)0x10000;
    var flags = native.RenderFlags;
    native.EntityId = 123;
    native.Position = new System.Numerics.Vector3(1, 2, 3);
    ModelVisibilityCodec.SetHidden(&native, true);
    Check(ModelVisibilityCodec.IsHidden(&native) && (native.RenderFlags & flags) == flags,
        "Hiding must set only the model bit and preserve unrelated render flags.");
    ModelVisibilityCodec.SetHidden(&native, false);
    Check(native.RenderFlags == flags && native.EntityId == 123 && native.Position.Y == 2,
        "Showing a model must preserve nameplate flags, identity, and position.");
}

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
    Check(catalog.All(h => h.NameplateIcon == 1), "Current S/A/B hunts must all use the data's elite nameplate category.");
    Check(catalog.All(h => LocalPreviewName.Matches(LocalPreviewName.Stamp(DisplayName.Encode(h.Name), 1), 1)),
        "Every current hunt name must leave room for local-preview ownership validation.");
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
    var actions = game.Excel.GetSheet<Lumina.Excel.Sheets.Action>(Language.ChineseSimplified);
    var timelines = game.Excel.GetSheet<ActionTimeline>();
    var skills = SkillCatalog.Load(actions, timelines);
    Console.WriteLine($"Playable action records: {skills.Count}");
    Check(skills.Count > 10000 && skills.All(p => p.Value.ActionId == p.Key && p.Value.TimelineId > 0
        && p.Value.Slot < 14 && !string.IsNullOrWhiteSpace(p.Value.TimelineKey)),
        "The skill catalog must contain only valid native timeline references.");
    Check(!skills.ContainsKey(0) && !skills.ContainsKey(9), "Player-only actions must not appear as NPC candidates.");
    var buf = catalog.Single(h => h.NameId == HuntSkillPresets.BufruNameId);
    var bufModel = game.Excel.GetSheet<ModelChara>().GetRow((uint)buf.ModelId);
    Check(buf.Name == "布弗鲁" && buf.ModelId == 3315 && SkillCatalog.ModelFamily(bufModel) == "m0741",
        "Bufru presets must resolve to the actual hunt and model resource.");
    var preset = HuntSkillPresets.For(buf.NameId, skills).ToArray();
    Check(preset.Select(s => s.Name).SequenceEqual(new[] { "巨魔碎颈臂", "五倍吸气", "五倍喷嚏", "上击", "腐烂孢子" }),
        "Bufru's curated list must match the supplied combat skills.");
    Check(preset.Select(s => s.TimelineId).SequenceEqual(new ushort[] { 3205, 3201, 3202, 3204, 3203 }),
        "Duplicate skill names from other encounters must not replace Bufru's action IDs.");
    foreach (var skill in preset)
    {
        var resource = skill.TimelineKey.Replace("[SKL_ID]", SkillCatalog.ModelFamily(bufModel));
        Check(game.FileExists($"chara/action/{resource}.tmb"), $"Missing Bufru action timeline resource: {resource}");
    }
    Check(!HuntSkillPresets.For(2953, skills).Any(), "Uncurated hunts must not inherit Bufru's skill list.");
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
            ModelScale = state.ModelScale, ObjectScale = state.ObjectScale, DrawScale = state.DrawScale, NameplateIcon = state.NameplateIcon };
}

sealed class FakeAnimationAccess : IAnimationAccess
{
    public List<(ActorIdentity Actor, int Model, ushort Timeline)> Calls { get; } = [];
    public void PlayAnimation(ActorIdentity actor, int expectedModelId, ushort timelineId)
        => Calls.Add((actor, expectedModelId, timelineId));
}

sealed class FakeLocalAccess(FakeAccess access) : ILocalActorAccess
{
    private ushort next = 300;
    private readonly Dictionary<LocalActorLease, byte> leases = [];
    public List<LocalActorLease> Deleted { get; } = [];
    public bool FailDelete { get; set; }
    public bool FailDraw { get; set; }
    public LocalSpawn Create(HuntAppearance hunt, float multiplier, float distance)
    {
        var index = next++;
        var actor = new ActorIdentity(index, (2UL << 32) | index, 0, 0, 0x10000 + index);
        var lease = new LocalActorLease((ushort)(index - 200), 0x5000, actor, index);
        var state = new VisualState(hunt.ModelId, -1, hunt.Scale * multiplier, hunt.Customize, hunt.Equipment,
            [0, 0, 0], 0, 0, LocalPreviewName.Stamp(DisplayName.Encode(hunt.Name), lease.Token), hunt.Level,
            hunt.Scale * multiplier, new System.Numerics.Vector3(hunt.Scale * multiplier));
        access.Actors.Add(actor, state);
        if (FailDraw) access.FailBeginFor = actor;
        leases.Add(lease, 0);
        return new(lease, state);
    }
    public bool Exists(LocalActorLease lease) => leases.ContainsKey(lease) && access.Actors.TryGetValue(lease.Actor, out var state)
        && LocalPreviewName.Matches(state.NameBytes, lease.Token);
    public void Delete(LocalActorLease lease)
    {
        if (FailDelete) throw new InvalidOperationException("Simulated delete failure.");
        if (!Exists(lease)) throw new Exception("Attempt to delete an unowned actor.");
        Deleted.Add(lease);
        leases.Remove(lease);
        access.Actors.Remove(lease.Actor);
        access.Hidden.Remove(lease.Actor);
    }
}

sealed class FakeVisibilityAccess : IModelVisibilityAccess
{
    public Dictionary<ActorIdentity, bool> Flags { get; } = [];
    public ActorIdentity? FailShowFor { get; set; }
    public int Writes { get; private set; }
    public bool Exists(ActorIdentity actor) => Flags.ContainsKey(actor);
    public bool IsHidden(ActorIdentity actor) => Flags[actor];
    public void SetHidden(ActorIdentity actor, bool hidden)
    {
        if (!hidden && FailShowFor == actor) throw new InvalidOperationException("Simulated visibility failure.");
        Flags[actor] = hidden;
        Writes++;
    }
}

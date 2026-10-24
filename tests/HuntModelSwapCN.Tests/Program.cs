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
    Check(draw.Object.IsTransformChanged, "A changed draw scale must invalidate cached render transforms.");
    draw.Object.IsTransformChanged = false;
    NativeAppearanceCodec.WriteScale(&native, source);
    Check(!draw.Object.IsTransformChanged, "An unchanged scale must not invalidate draw transforms every frame.");
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

// Regression: spawned actors used friendly green nameplates despite the correct name/level/icon data.
// A local preview's cached label anchor must track its actual render scale.
unsafe
{
    NativeCharacter localNative = default;
    FFXIVClientStructs.FFXIV.Client.Graphics.Scene.DrawObject draw = default;
    localNative.GameObject.DrawObject = &draw;
    localNative.GameObject.Position = new(12, 34, 56);
    localNative.GameObject.DrawOffset = new(0.2f, 0.4f, -0.3f);
    localNative.GameObject.NameplateOffsetScaleMultiplier = 0.7f;
    localNative.GameObject.NameplateOffset = new(0, 2, 0);
    localNative.GameObject.NameplateOffsetTarget = new(0, 2, 0);
    var attachment = new System.Numerics.Vector3(0.5f, 4, -0.5f);
    draw.Object.Scale = new(1);
    Check(NativeLocalNameplateCodec.Write(&localNative, attachment, 0.6f)
        && (System.Numerics.Vector3)localNative.GameObject.NameplateOffset == new System.Numerics.Vector3(0.7f, 5, -0.8f),
        "A loaded model's native attachment plus draw/height offsets must determine its label anchor.");
    draw.Object.Scale = new(3);
    Check(NativeLocalNameplateCodec.Write(&localNative, attachment, 0.6f)
        && localNative.GameObject.NameplateOffset.Y == 13
        && localNative.GameObject.NameplateOffsetTarget.Y == 13,
        "Enlarging a preview must move both the rendered label and interpolation target above the enlarged model.");
    Check(!NativeLocalNameplateCodec.Write(&localNative, attachment, 0.6f)
        && localNative.GameObject.NameplateOffset.Y == 13,
        "Repeated frame updates must not compound the previous label height.");
    draw.Object.Scale = new(0.5f);
    Check(NativeLocalNameplateCodec.Write(&localNative, attachment, 0.6f)
        && localNative.GameObject.NameplateOffset.Y == 3,
        "Shrinking after enlargement must use the unscaled attachment again, not a cached scaled offset.");
    Check(NativeLocalNameplateCodec.Write(&localNative, new(0, 10, 0), 0.6f)
        && localNative.GameObject.NameplateOffset.Y == 6,
        "Changing the preview model must use the new model's attachment height.");
    var validAnchor = localNative.GameObject.NameplateOffset;
    Check(!NativeLocalNameplateCodec.Write(&localNative, new(float.NaN, 1, 0), 0.6f)
        && (System.Numerics.Vector3)localNative.GameObject.NameplateOffset == (System.Numerics.Vector3)validAnchor,
        "Invalid model attachment data must leave the last valid native anchor intact.");
    draw.Object.Scale = new(float.PositiveInfinity);
    Check(!NativeLocalNameplateCodec.Write(&localNative, attachment, 0.6f),
        "Non-finite transforms must never be written into nameplate coordinates.");
    localNative.GameObject.DrawObject = null;
    Check(!NativeLocalNameplateCodec.Write(&localNative, attachment, 0.6f),
        "An unloaded preview must wait for a model before updating its nameplate height.");
    Check(localNative.GameObject.Position.Y == 34 && localNative.GameObject.NameplateOffsetScaleMultiplier == 0.7f
        && localNative.GameObject.Scale == 0 && localNative.GameObject.Height == 0,
        "Label correction must preserve placement, native extra nameplate gap and gameplay height/scale fields.");
}

// Passive combat NPCs such as 蜜蜂云 and 跳蜥幼体 were rejected
// by a hostile-flag check even though the user explicitly selected the creature.
unsafe
{
    NativeCharacter passive = default;
    passive.GameObject.ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.BattleNpc;
    passive.GameObject.SubKind = 5;
    foreach (var nameId in new uint[] { 395, 401, 1142 })
    {
        passive.NameId = nameId;
        passive.IsHostile = false;
        passive.GameObject.OwnerId = 0xe0000000;
        Check(ModelVisibilityCodec.CannotHideSelected(&passive.GameObject) is null,
            "Explicitly selected passive combat creatures must be hideable without setting hostile or combat flags.");
        ModelVisibilityCodec.SetHidden(&passive.GameObject, true);
        Check(ModelVisibilityCodec.IsHidden(&passive.GameObject) && !passive.IsHostile && passive.NameId == nameId,
            "Hiding a passive creature must not make it aggressive or change its identity.");
        ModelVisibilityCodec.SetHidden(&passive.GameObject, false);
    }
    passive.GameObject.OwnerId = 0;
    Check(ModelVisibilityCodec.CannotHideSelected(&passive.GameObject) is null,
        "Unowned passive NPCs can also use the reversible model visibility control.");
    passive.GameObject.OwnerId = 123;
    Check(ModelVisibilityCodec.CannotHideSelected(&passive.GameObject) is not null,
        "Removing the hostile requirement must not allow hiding another character's pet.");
    passive.GameObject.OwnerId = 0;
    passive.GameObject.SubKind = 2;
    Check(ModelVisibilityCodec.CannotHideSelected(&passive.GameObject) is not null,
        "Non-combat battle objects remain outside the selected-monster hide action.");
    passive.GameObject.SubKind = 5;
    passive.GameObject.ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc;
    Check(ModelVisibilityCodec.CannotHideSelected(&passive.GameObject) is not null
        && ModelVisibilityCodec.CannotHideSelected(null) is not null,
        "An unrelated player or absent target must not pass selected-monster validation.");
}

unsafe
{
    NativeCharacter localNative = default;
    localNative.Flags = 0xa0;
    localNative.Battalion = 0;
    localNative.NameId = 0;
    localNative.GameObject.BaseId = 0;
    localNative.Health = localNative.MaxHealth = 1;
    localNative.Level = 90;
    localNative.Icon = 1;
    localNative.ModelScale = 3;
    localNative.GameObject.Position = new(1, 2, 3);
    localNative.GameObject.UpdateFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Unk2;
    Check(NativeLocalPresentationCodec.Write(&localNative, 4) && localNative.IsHostile && localNative.Battalion == 4,
        "Owned previews must be classified as enemies using the selected hunt's battalion.");
    Check(localNative.Flags == 0xa1 && !localNative.InCombat,
        "Enemy presentation must preserve unrelated character flags and must not enter combat.");
    Check(localNative.NameId == 0 && localNative.GameObject.BaseId == 0 && localNative.Health == 1
        && localNative.MaxHealth == 1 && localNative.Level == 90 && localNative.Icon == 1
        && localNative.ModelScale == 3 && localNative.GameObject.Position.Y == 2,
        "Enemy styling must preserve client-only identity, health, level, elite icon, scale and placement.");
    Check(localNative.GameObject.UpdateFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Name)
        && localNative.GameObject.UpdateFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Unk2),
        "Changing preview classification must refresh its nameplate without clearing unrelated update flags.");
    localNative.GameObject.UpdateFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Unk2;
    Check(!NativeLocalPresentationCodec.Write(&localNative, 4)
        && !localNative.GameObject.UpdateFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Name),
        "Unchanged enemy presentation must not dirty or request a nameplate redraw every frame.");
    localNative.IsHostile = false;
    localNative.Battalion = 1;
    Check(NativeLocalPresentationCodec.Write(&localNative, 4) && localNative.IsHostile && localNative.Battalion == 4,
        "A client refresh must not permanently turn an owned preview back into a friendly green NPC.");
}

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

var comboAccess = new FakeAccess();
comboAccess.Actors.Add(actor, original);
var comboSession = new SwapSession(comboAccess);
comboSession.Apply(actor, "小怪", "布弗鲁", State(3315, 3));
comboSession.Tick();
var comboAnimation = new FakeAnimationAccess();
comboAnimation.Rotations[actor] = 0.4f;
var comboPlayback = new ActionPreview(comboSession, comboAnimation);
var eligible = true;
var combo = new BufruSequence(comboSession, comboPlayback, comboAnimation, _ => eligible);
var comboSkills = new Dictionary<uint, SkillAnimation>
{
    [27307] = new(27307, "五倍吸气", 3201, "mon_sp/[SKL_ID]/mon_sp001", 1, 4000),
    [27308] = new(27308, "五倍吸气", 3201, "mon_sp/[SKL_ID]/mon_sp001", 1, 300),
    [27309] = new(27309, "五倍喷嚏", 3202, "mon_sp/[SKL_ID]/mon_sp002", 1, 5000),
    [27310] = new(27310, "五倍喷嚏", 3202, "mon_sp/[SKL_ID]/mon_sp002", 1, 300),
};
var comboDirections = new[] { 0, 1, 3, 2, 0 };
combo.Start(actor, comboSkills, comboDirections, 1000, 0);
Check(combo.Running && combo.DirectionOrder == "前 → 右 → 左 → 后 → 前" && combo.RemainingSeconds(1000) == 3,
    "A combo must record exactly five directions relative to its initial heading and expose cast progress.");
combo.Tick(3999);
Check(comboAnimation.Calls.Count == 0, "The first inhale must wait for its four-second cast.");
long comboNow = 4000;
for (var step = 0; step < 10; step++)
{
    if (step > 0)
    {
        comboNow += 1000;
        combo.Tick(comboNow); // Begin the next cast after the preceding animation's hold.
        comboNow += step == 5 ? 5000 : 300;
    }
    combo.Tick(comboNow);
    var played = comboAnimation.DirectionalPlays.Last();
    var expectedHeading = 0.4f + comboDirections[step % 5] * MathF.PI / 2;
    Check(comboAnimation.Calls.Count == step + 1 && played.Timeline == (step < 5 ? 3201 : 3202)
        && MathF.Abs(MathF.IEEERemainder(played.Rotation - expectedHeading, MathF.Tau)) < 0.001f,
        $"Combo step {step + 1} must play the correct inhale/sneeze timeline in the memorized direction.");
}
combo.Tick(comboNow + 1000);
Check(!combo.Running && comboAnimation.Calls.Count == 10 && MathF.Abs(comboAnimation.Rotations[actor] - 0.4f) < 0.001f,
    "Completing a combo must play exactly ten actions and restore the initial heading.");
combo.Start(actor, comboSkills, comboDirections, 1000, 100000);
combo.Tick(104000);
combo.Stop();
var callsAfterStop = comboAnimation.Calls.Count;
combo.Tick(120000);
Check(!combo.Running && comboAnimation.Calls.Count == callsAfterStop && MathF.Abs(comboAnimation.Rotations[actor] - 0.4f) < 0.001f,
    "Stopping must cancel every remaining step and restore facing without scheduling more animations.");
combo.Start(actor, comboSkills, comboDirections, 1000, 200000);
var writesBeforeScene = comboAnimation.FacingWrites;
combo.ForgetScene();
combo.Tick(220000);
Check(!combo.Running && combo.DirectionOrder == "" && comboAnimation.FacingWrites == writesBeforeScene,
    "A scene change must discard the combo without writing the old actor's rotation.");
combo.Start(actor, comboSkills, comboDirections, 1000, 300000);
eligible = false; // Same identity, but a local actor has lost its ownership nonce.
var writesBeforeOwnershipLoss = comboAnimation.FacingWrites;
combo.Tick(300001);
Check(!combo.Running && comboAnimation.FacingWrites == writesBeforeOwnershipLoss,
    "Losing local ownership must cancel the combo without restoring rotation into a recycled actor.");
eligible = true;
combo.Start(actor, comboSkills, comboDirections, 1000, 400000);
comboAnimation.Unavailable = true;
try { combo.Tick(400001); throw new Exception("Expected unavailable animation actor."); } catch (InvalidOperationException) { }
combo.Tick(410000);
Check(!combo.Running && comboAnimation.Calls.Count == callsAfterStop,
    "An animation wrapper failure must stop the sequence once and prevent repeated playback attempts.");
comboAnimation.Unavailable = false;
combo.Start(actor, comboSkills, comboDirections, 1000, 500000);
combo.Tick(600000);
var afterStall = comboAnimation.Calls.Count;
combo.Tick(600001);
combo.Tick(601000);
Check(comboAnimation.Calls.Count == afterStall,
    "A long stalled frame must not burst all missed steps or skip a visible cast phase.");
combo.Tick(601300);
Check(comboAnimation.Calls.Count == afterStall + 1, "The next action must resume after its own cast and hold timing.");
combo.Stop();
var callsBeforeInvalid = comboAnimation.Calls.Count;
foreach (var (directions, hold) in new[] { (new[] { 0, 1, 2, 3 }, 1000), (new[] { 0, 1, 2, 3, 4 }, 1000),
    (comboDirections, 749), (comboDirections, 10001) })
    try { combo.Start(actor, comboSkills, directions, hold, 700000); throw new Exception("Invalid combo accepted."); }
    catch (InvalidOperationException) { tests++; }
var missingComboSkills = comboSkills.Where(p => p.Key != 27308).ToDictionary(p => p.Key, p => p.Value);
try { combo.Start(actor, missingComboSkills, comboDirections, 1000, 700000); throw new Exception("Incomplete combo accepted."); }
catch (InvalidOperationException) { tests++; }
comboSession.Apply(actor, "小怪", "其他怪", State(235, 3));
comboSession.Tick();
try { combo.Start(actor, comboSkills, comboDirections, 1000, 700000); throw new Exception("Wrong model combo accepted."); }
catch (InvalidOperationException) { Check(comboAnimation.Calls.Count == callsBeforeInvalid, "Only a completed Bufru appearance may start its combo."); }

var resizeAccess = new FakeAccess();
resizeAccess.Actors.Add(actor, original);
var resizeSession = new SwapSession(resizeAccess);
try { resizeSession.Resize(actor, 2); throw new Exception("Untracked resize accepted."); }
catch (InvalidOperationException) { tests++; }
resizeSession.Apply(actor, "小怪", "布弗鲁", State(3315, 3));
try { resizeSession.Resize(actor, 2); throw new Exception("Pending redraw resize accepted."); }
catch (InvalidOperationException) { tests++; }
resizeSession.Tick();
resizeSession.Resize(actor, 12.75f);
Check(resizeAccess.Actors[actor].ModelScale == 12.75f && resizeAccess.Actors[actor].ObjectScale == 12.75f
    && resizeAccess.Actors[actor].DrawScale == new System.Numerics.Vector3(12.75f),
    "Free sizing must use the entered absolute scale without multiplying by the hunt scale or clamping to three.");
resizeSession.Tick();
Check(resizeAccess.Actors[actor].ModelScale == 12.75f && resizeSession.Entries.Single().Original == original,
    "A resized preview must retain its new size on refresh and preserve the first original snapshot.");
foreach (var invalidScale in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
{
    try { resizeSession.Resize(actor, invalidScale); throw new Exception("Invalid resize accepted."); }
    catch (InvalidOperationException) { Check(resizeSession.Entries.Single().Applied.ModelScale == 12.75f,
        "Invalid sizes must not replace the last valid state."); }
}
resizeSession.Resize(actor, 0.025f);
Check(resizeAccess.Actors[actor].ModelScale == 0.025f && resizeAccess.Actors[actor].ModelId == 3315
    && resizeAccess.Actors[actor].NameBytes.SequenceEqual(State(3315, 3).NameBytes),
    "Small positive scales must be accepted without changing the selected model or name.");
resizeSession.Restore(actor);
try { resizeSession.Resize(actor, 2); throw new Exception("Restoring resize accepted."); }
catch (InvalidOperationException) { tests++; }
resizeSession.Tick();
Check(resizeAccess.Actors[actor] == original, "Restoring after free resizing must restore the full original appearance and scale.");
resizeSession.Apply(actor, "小怪", "布弗鲁", State(3315, 3));
resizeSession.Tick();
resizeAccess.Actors.Remove(actor);
resizeAccess.Actors.Add(reused, original);
try { resizeSession.Resize(actor, 2); throw new Exception("Stale resize accepted."); }
catch (InvalidOperationException) { Check(resizeAccess.Actors[reused] == original, "Free resizing must reject recycled identities."); }

var localAccess = new FakeAccess();
var localSession = new SwapSession(localAccess);
var localFactory = new FakeLocalAccess(localAccess);
var localPreview = new LocalPreview(localSession, localFactory);
var sampleHunt = new HuntAppearance(10617, 13834, 3315, 3, 4, 961, "布弗鲁", "厄尔庇斯", 3, 3,
    new byte[26], new ulong[10], 0, 0, false, Battalion: 4);
var firstLocal = localPreview.Spawn(sampleHunt, sampleHunt.Scale, 8);
Check(localSession.Entries.Single().Applied.ModelScale == sampleHunt.Scale,
    "The selected hunt's own native scale must be usable directly as the default spawn size.");
try { localPreview.Spawn(sampleHunt, float.NaN, 8); throw new Exception("Invalid spawn scale accepted."); }
catch (InvalidOperationException) { Check(localPreview.Owns(firstLocal) && localFactory.Deleted.Count == 0,
    "An invalid spawn scale must be refused before removing an existing preview."); }
Check(localPreview.Owns(firstLocal) && localSession.Entries.Count == 1 && firstLocal.DataId == 0 && firstLocal.NameId == 0,
    "A local preview must be tracked independently and have no live hunt identity.");
localPreview.Tick();
Check(localFactory.Refreshed.Single().Battalion == 4,
    "A live owned preview must maintain the selected hunt's enemy presentation relationship.");
Check(localPreview.NameplateVisibility(firstLocal, 0x107) == 0x105,
    "Local preview nameplates must hide only the HP gauge, preserving name text and unrelated visibility bits.");
Check(localPreview.NameplateVisibility(actor, 0x107) == 0x107,
    "Real monsters, including replaced ones, must retain their HP gauge settings.");
Check(localPreview.NameplateVisibility(firstLocal, 0x104) == 0x104,
    "Hiding preview HP must not force previously hidden name text visible.");
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
Check(localPreview.NameplateVisibility(secondLocal, 0x107) == 0x107,
    "Removing a preview must stop overriding its old nameplate immediately.");
var recycledLocal = localPreview.Spawn(sampleHunt, 1, 8);
localAccess.Actors[recycledLocal] = State(12, 1); // Same slot/address/IDs, but a different owner.
var refreshCount = localFactory.Refreshed.Count;
Check(localPreview.NameplateVisibility(recycledLocal, 0x107) == 0x107,
    "A reused slot must retain its gauge even before the next ownership cleanup tick.");
localPreview.Tick();
Check(localFactory.Refreshed.Count == refreshCount,
    "A lost ownership marker must stop enemy-presentation writes before touching a recycled actor.");
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
var notRedrawing = new HashSet<ActorIdentity>();
visibility.SetHideSelf(true);
visibility.Tick(actor, notRedrawing);
Check(visibilityAccess.Flags[actor] && !visibilityAccess.Flags[other] && visibility.Count == 1,
    "The self-only switch must not hide unrelated monsters.");
visibility.Hide(other);
visibility.Hide(other);
visibility.Tick(actor, new HashSet<ActorIdentity> { other });
Check(!visibilityAccess.Flags[other] && visibility.Count == 1,
    "Hiding a selected monster must wait until its temporary model redraw finishes.");
visibility.Tick(actor, notRedrawing);
Check(visibilityAccess.Flags[other] && visibility.Count == 2,
    "Only the selected monster and explicitly hidden self must become hidden.");
visibility.SetHideSelf(false);
visibility.Tick(actor, notRedrawing);
Check(!visibilityAccess.Flags[actor] && visibilityAccess.Flags[other],
    "Disabling self hiding must preserve individually hidden monsters.");
visibilityAccess.Flags[other] = false;
visibility.Tick(actor, notRedrawing);
Check(visibilityAccess.Flags[other], "An individually hidden monster must stay hidden after a game refresh.");
visibility.Hide(reused);
visibility.Tick(actor, notRedrawing);
Check(visibility.ManagedActors.Count() == 2, "Multiple selected monsters must be tracked without duplicate entries.");
visibility.Show(other);
visibility.Tick(actor, new HashSet<ActorIdentity> { other });
Check(visibilityAccess.Flags[other] && visibility.Count == 2,
    "Restoring a monster during a redraw must wait for redraw completion.");
visibility.Tick(actor, notRedrawing);
Check(!visibilityAccess.Flags[other] && visibilityAccess.Flags[reused] && visibility.Count == 1,
    "Restoring one selected monster must leave other hidden monsters unchanged.");
visibility.Reset();
Check(!visibilityAccess.Flags[actor] && !visibilityAccess.Flags[other] && visibilityAccess.Flags[reused] && visibility.Count == 0
    && !visibility.Active && !visibility.ManagedActors.Any(),
    "Reset must preserve pre-existing hidden state and clear individual selections.");
visibility.Hide(other);
visibility.Tick(actor, notRedrawing);
visibilityAccess.Flags.Remove(other);
var newOwner = other with { ObjectId = 777, NameId = 0 };
visibilityAccess.Flags.Add(newOwner, false);
var beforeReused = visibilityAccess.Writes;
visibility.Tick(actor, notRedrawing);
Check(!visibility.Active && !visibility.ManagedActors.Any() && !visibilityAccess.Flags[newOwner]
    && visibilityAccess.Writes == beforeReused,
    "A disappeared monster must be forgotten without hiding a new owner of the same slot.");
visibility.Hide(other); // A queued request for the old identity.
visibility.Tick(actor, notRedrawing);
Check(!visibilityAccess.Flags[newOwner] && !visibility.Active,
    "A stale individual hide request must not affect a recycled identity.");
visibilityAccess.Flags.Add(other, false);
visibility.Hide(other);
visibility.Tick(actor, notRedrawing);
visibilityAccess.FailShowFor = other;
try { visibility.Reset(); throw new Exception("Expected visibility restore failure."); } catch (AggregateException) { }
Check(visibility.Count == 1 && !visibility.Active && visibility.ManagedActors.Contains(other),
    "A failed restore must retain the failing actor in the list for retry.");
visibilityAccess.FailShowFor = null;
visibility.Tick(actor, notRedrawing);
Check(visibility.Count == 0 && !visibilityAccess.Flags[other], "Visibility cleanup must be retryable on the next update.");
visibility.Hide(other);
visibility.Tick(actor, notRedrawing);
visibility.Hide(actor); // Pending hide, not written yet.
var writeCount = visibilityAccess.Writes;
visibility.ForgetScene();
visibility.Tick(actor, notRedrawing);
Check(!visibility.Active && visibility.Count == 0 && !visibility.ManagedActors.Any() && visibilityAccess.Writes == writeCount,
    "Discarding an old scene must clear pending selections without writing into a new scene.");
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
    Check(catalog.All(h => h.Battalion == 4), "Regular hunt previews must use the enemy battalion read from their own BNpcBase records.");
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
    Check(new uint[] { 27307, 27308, 27309, 27310 }.Select(id => skills[id].CastTimeMs).SequenceEqual(new[] { 4000, 300, 5000, 300 }),
        "Bufru combo cast times must come from the CN action data, including the short repeat actions.");
    Check(skills[27308].TimelineId == skills[27307].TimelineId && skills[27310].TimelineId == skills[27309].TimelineId,
        "Bufru repeat actions must resolve to the same inhale/sneeze resources as the first cast.");
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
    Check(HuntSkillPresets.For(2953, skills).Any() && HuntSkillPresets.For(2953, skills).All(s => !preset.Any(b => b.ActionId == s.ActionId)),
        "Laideronnette must have its own Wiki skill mappings, not inherit Bufru's actions.");
    Check(HuntSkillPresets.All.Count == 47 && HuntSkillPresets.All.Sum(p => p.Skills.Length) == 300,
        "Every current regular S hunt must have its reviewed Wiki skill profile.");
    Check(HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "record")) == 138
        && HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "resource")) == 116
        && HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "variants")) == 34
        && HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "unavailable")) == 12,
        "Known action records, unique resource mappings, ambiguous variants and unavailable releases must stay distinct.");
    foreach (var skillProfile in HuntSkillPresets.All)
    {
        var hunt = catalog.Single(h => h.NameId == skillProfile.NameId);
        var family = SkillCatalog.ModelFamily(game.Excel.GetSheet<ModelChara>().GetRow((uint)hunt.ModelId));
        Check(hunt.Rank == 3 && hunt.Name == skillProfile.Name && hunt.ModelId == skillProfile.ModelId
            && skillProfile.Skills.Select(s => s.Name).Distinct().Count() == skillProfile.Skills.Length
            && skillProfile.WikiUrl.StartsWith("https://ff14.huijiwiki.com/wiki/", StringComparison.Ordinal),
            $"Skill skillProfiles must identify the correct S hunt and retain its Wiki source: {skillProfile.Name}");
        Check(skillProfile.Skills.Any(s => HuntSkillPresets.Resolve(s.Name, s.Primary, skills) is not null),
            $"Every current S hunt must have at least one directly available animation mapping: {skillProfile.Name}");
        foreach (var item in skillProfile.Skills)
        {
            Check(item.Mode is "record" or "resource" or "variants" or "unavailable"
                && ((item.Mode is "record" or "resource") == (item.Primary is not null))
                && (item.Mode != "variants" || item.Variants.Length > 1)
                && (item.Mode != "unavailable" || item.Variants.Length == 0),
                $"Unavailable or ambiguous Wiki entries must never silently become primary actions: {skillProfile.Name}/{item.Name}");
            foreach (var mapping in item.Variants.Concat(item.Primary is { } primary ? new[] { primary } : []).Distinct())
            {
                var resolved = HuntSkillPresets.Resolve(item.Name, mapping, skills);
                Check(resolved is not null && !actions.GetRow(mapping.ActionId).IsPlayerAction,
                    $"Every mapped action must still match its CN name and timeline: {skillProfile.Name}/{item.Name}");
                var resource = mapping.TimelineKey.Replace("[SKL_ID]", family);
                Check((mapping.TimelineKey.StartsWith($"mon_sp/{family}/", StringComparison.Ordinal)
                    || mapping.TimelineKey.StartsWith("mon_sp/[SKL_ID]/", StringComparison.Ordinal)
                    || mapping.TimelineKey.StartsWith("mon_sp/common/", StringComparison.Ordinal)
                    || mapping.TimelineKey.StartsWith("magic/", StringComparison.Ordinal))
                    && game.FileExists($"chara/action/{resource}.tmb"),
                    $"Each skillProfile action must have a compatible installed resource: {skillProfile.Name}/{item.Name}");
            }
        }
    }
    var testMapping = HuntSkillPresets.Profile(10617)!.Skills.Single(s => s.Name == "五倍吸气").Primary!;
    var changedSkills = skills.ToDictionary();
    changedSkills[testMapping.ActionId] = changedSkills[testMapping.ActionId] with { Name = "已变更的技能" };
    Check(HuntSkillPresets.Resolve("五倍吸气", testMapping, changedSkills) is null,
        "A changed client action name must disable a curated mapping rather than play an unrelated action.");
    foreach (var (nameId, wikiName, cnName) in new[] { (8900u, "迅速回旋", "迅猛回旋"),
        (8895u, "轮转枪", "转轮枪"), (2969u, "等离子立场", "等离子力场") })
    {
        var alias = HuntSkillPresets.Profile(nameId)!.Skills.Single(s => s.Name == wikiName).Primary!;
        Check(alias.ActionName == cnName && HuntSkillPresets.Resolve(wikiName, alias, skills)?.Name == cnName,
            "Reviewed Wiki spellings must resolve to the current CN action without dropping the skill.");
        var renamed = skills.ToDictionary();
        renamed[alias.ActionId] = renamed[alias.ActionId] with { Name = "不匹配" };
        Check(HuntSkillPresets.Resolve(wikiName, alias, renamed) is null,
            "An alias must still validate the current CN action name.");
    }
    Check(HuntSkillPresets.Profile(0) is null && !HuntSkillPresets.For(0, skills).Any(),
        "Unmapped actors must not inherit another hunt's curated skill list.");
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

sealed class FakeAnimationAccess : IAnimationAccess, IDirectionalAnimationAccess
{
    public List<(ActorIdentity Actor, int Model, ushort Timeline)> Calls { get; } = [];
    public Dictionary<ActorIdentity, float> Rotations { get; } = [];
    public List<(ActorIdentity Actor, ushort Timeline, float Rotation)> DirectionalPlays { get; } = [];
    public int FacingWrites { get; private set; }
    public bool Unavailable { get; set; }
    public void PlayAnimation(ActorIdentity actor, int expectedModelId, ushort timelineId)
    {
        if (Unavailable) throw new InvalidOperationException("Simulated missing animation actor.");
        Calls.Add((actor, expectedModelId, timelineId));
        DirectionalPlays.Add((actor, timelineId, Rotations.GetValueOrDefault(actor)));
    }
    public float Rotation(ActorIdentity actor, int expectedModelId)
        => !Unavailable ? Rotations.GetValueOrDefault(actor) : throw new InvalidOperationException("Simulated missing actor.");
    public void Face(ActorIdentity actor, int expectedModelId, float rotation)
    {
        if (Unavailable) throw new InvalidOperationException("Simulated missing actor.");
        Rotations[actor] = rotation;
        FacingWrites++;
    }
}

sealed class FakeLocalAccess(FakeAccess access) : ILocalActorAccess
{
    private ushort next = 300;
    private readonly Dictionary<LocalActorLease, byte> leases = [];
    public List<LocalActorLease> Deleted { get; } = [];
    public List<LocalActorLease> Refreshed { get; } = [];
    public bool FailDelete { get; set; }
    public bool FailDraw { get; set; }
    public LocalSpawn Create(HuntAppearance hunt, float scale, float distance)
    {
        var index = next++;
        var actor = new ActorIdentity(index, (2UL << 32) | index, 0, 0, 0x10000 + index);
        var lease = new LocalActorLease((ushort)(index - 200), 0x5000, actor, index, hunt.Battalion);
        var state = ModelSize.Apply(new VisualState(hunt.ModelId, -1, hunt.Scale, hunt.Customize, hunt.Equipment,
            [0, 0, 0], 0, 0, LocalPreviewName.Stamp(DisplayName.Encode(hunt.Name), lease.Token), hunt.Level,
            hunt.Scale, new System.Numerics.Vector3(hunt.Scale)), scale);
        access.Actors.Add(actor, state);
        if (FailDraw) access.FailBeginFor = actor;
        leases.Add(lease, 0);
        return new(lease, state);
    }
    public bool Exists(LocalActorLease lease) => leases.ContainsKey(lease) && access.Actors.TryGetValue(lease.Actor, out var state)
        && LocalPreviewName.Matches(state.NameBytes, lease.Token);
    public void RefreshPresentation(LocalActorLease lease)
    {
        if (!Exists(lease)) throw new Exception("Attempt to update an unowned actor.");
        Refreshed.Add(lease);
    }
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

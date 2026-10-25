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

// Regression from the actual disabled-button report: the old CanApply required
// Combatant=5 and a non-null draw object even for known training dummies.
{
    foreach (var name in new uint[] { 541, 13078 })
        foreach (var kind in new byte[] { 0, 1, 5, 9 })
        {
            var facts = new ReplacementTargetFacts(true, kind, name, 0xe0000000, 1, false, false);
            Check(ReplacementTargetPolicy.CannotApply(facts) is null,
                "A verified live dummy must be replaceable with a non-Combatant subkind and before its draw object exists.");
            Check(ReplacementTargetPolicy.CannotApply(facts with { OwnerId = 0x12345678 }) is null,
                "A known dummy's housing/parent owner association must not classify it as a player pet.");
        }
    var ordinary = new ReplacementTargetFacts(true, 5, 33, 0xe0000000, 1, false, true);
    Check(ReplacementTargetPolicy.CannotApply(ordinary) is null, "Existing ordinary combatant replacement must remain allowed.");
    Check(ReplacementTargetPolicy.CannotApply(ordinary with { SubKind = 2, OwnerId = 0x12345678 }) is not null
        && ReplacementTargetPolicy.CannotApply(ordinary with { SubKind = 9 }) is not null
        && ReplacementTargetPolicy.CannotApply(ordinary with { HasDrawObject = false }) is not null
        && ReplacementTargetPolicy.CannotApply(ordinary with { OwnerId = 0x12345678 }) is not null,
        "Pets, party NPCs, owned ordinary actors and unloaded ordinary enemies must retain their existing exclusions.");
    var dummy = ordinary with { NameId = 541, SubKind = 0, HasDrawObject = false };
    Check(ReplacementTargetPolicy.CannotApply(dummy with { IsBattleNpc = false }) is not null
        && ReplacementTargetPolicy.CannotApply(dummy with { CurrentHp = 0 }) is not null
        && ReplacementTargetPolicy.CannotApply(dummy with { IsTransformed = true }) is not null,
        "A dummy identity must not bypass native type, health or active transformation checks.");
    unsafe
    {
        NativeCharacter native = default;
        native.NameId = 541; native.GameObject.BaseId = 901;
        native.ModelContainer.ModelCharaId = 2449;
        native.ModelContainer.ModelCharaId_2 = -1;
        native.GameObject.Scale = native.ModelScale = 1;
        native.Level = 1; native.Health = 100; native.MaxHealth = 100;
        DisplayName.Encode("木人").CopyTo(native.GameObject.Name);
        var before = NativeAppearanceCodec.Capture(&native);
        NativeAppearanceCodec.Validate(before);
        Check(before.DrawScale == new System.Numerics.Vector3(1),
            "Initial dummy capture must use object scale without dereferencing a missing draw object.");
        NativeAppearanceCodec.Write(&native, State(235, 3.5f) with { NameBytes = DisplayName.Encode("雷德罗巨蛇"), Level = 50, NameplateIcon = 1 });
        Check(native.GameObject.DrawObject == null && native.ModelContainer.ModelCharaId == 235
            && native.GameObject.NameString == "雷德罗巨蛇" && native.Level == 50 && native.Icon == 1 && native.ModelScale == 3.5f,
            "A missing dummy draw object must still permit complete appearance preparation before EnableDraw.");
        NativeAppearanceCodec.Write(&native, before);
        Check(native.GameObject.DrawObject == null && native.ModelContainer.ModelCharaId == 2449
            && native.GameObject.NameString == "木人" && native.Level == 1 && native.ModelScale == 1
            && native.NameId == 541 && native.GameObject.BaseId == 901 && native.Health == 100,
            "Restoring an initially draw-less dummy must preserve its complete original appearance and identity.");
    }
}

// Dummy reset uses the same actor identity. Keep the first dummy snapshot and
// complete the replacement's redraw on the next frame, including after retries.
{
    var dummy = actor with { DataId = 901, NameId = 541 };
    var dummyOriginal = State(2449, 1) with { NameBytes = DisplayName.Encode("木人"), Level = 100, NameplateIcon = 0 };
    var dummyAccess = new FakeAccess { RepairDummies = true };
    dummyAccess.Actors.Add(dummy, dummyOriginal);
    var dummySession = new SwapSession(dummyAccess);
    dummySession.Apply(dummy, "木人", "雷德罗巨蛇", State(235, 3) with { Level = 50, NameplateIcon = 1 });
    dummySession.Tick();
    dummyAccess.Actors[dummy] = dummyOriginal;
    dummySession.Tick();
    Check(dummySession.Entries.Single().WaitingForDraw && dummyAccess.Hidden.Contains(dummy)
        && dummyAccess.Actors[dummy].ModelId == 235, "A reset dummy must reapply its model using the pending two-frame redraw.");
    dummySession.Tick();
    Check(!dummyAccess.Hidden.Contains(dummy) && !dummySession.Entries.Single().WaitingForDraw
        && dummySession.Entries.Single().Original == dummyOriginal,
        "Repair must finish on the next frame without capturing the replaced creature as the original dummy.");
    dummyAccess.Actors[dummy] = dummyOriginal;
    dummyAccess.FailBeginFor = dummy;
    try { dummySession.Tick(); throw new Exception("Expected repair redraw failure."); }
    catch (InvalidOperationException) { }
    dummyAccess.FailBeginFor = null;
    dummySession.Tick(); dummySession.Tick();
    Check(dummyAccess.Actors[dummy].ModelId == 235 && !dummyAccess.Hidden.Contains(dummy),
        "A failed repair must remain tracked and be recoverable on a subsequent tick.");
    dummySession.Apply(dummy, "雷德罗巨蛇", "B怪", State(300, 0.5f) with { Level = 80, NameplateIcon = 1 });
    dummySession.Tick();
    Check(NameplateLevelPolicy.Resolve(dummySession.Entries.Single()) == "80级 ", "Changing a dummy from S to B must update the cached nameplate level.");
    dummySession.Restore(dummy);
    Check(NameplateLevelPolicy.Resolve(dummySession.Entries.Single()) == "100级 ", "A restoring dummy must use its original level prefix immediately.");
    dummySession.Tick();
    Check(dummyAccess.Actors[dummy] == dummyOriginal && dummySession.Entries.Count == 0,
        "Repeated swaps and automatic repairs must still restore the original dummy's complete presentation.");
    var writes = dummyAccess.Writes.Count;
    dummySession.Tick();
    Check(dummyAccess.Writes.Count == writes, "Dummy repair must stop when restore has removed the swap entry.");
    Check(!StrikingDummyPolicy.NeedsRedraw(33, 2449, -1, false, State(235, 3))
        && !StrikingDummyPolicy.NeedsRedraw(541, 2449, -1, true, State(235, 3)),
        "Automatic dummy repair must not apply to ordinary monsters or native transformations.");
    Check(NameplateLevelPolicy.Resolve(null) is null, "Untracked objects must retain the game's level text.");
}

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
    Check(NativeLocalPresentationCodec.Write(&localNative, 4, true) && localNative.IsHostile && localNative.Battalion == 4,
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
    Check(!NativeLocalPresentationCodec.Write(&localNative, 4, true)
        && !localNative.GameObject.UpdateFlags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Name),
        "Unchanged enemy presentation must not dirty or request a nameplate redraw every frame.");
    localNative.IsHostile = false;
    localNative.Battalion = 1;
    Check(NativeLocalPresentationCodec.Write(&localNative, 4, true) && localNative.IsHostile && localNative.Battalion == 4,
        "A client refresh must not permanently turn an owned preview back into a friendly green NPC.");
    localNative.Flags = 0xa3; // Preserve combat and other bits when changing just aggression.
    Check(NativeLocalPresentationCodec.Write(&localNative, 4, false) && !localNative.IsHostile
        && localNative.Battalion == 4 && localNative.Flags == 0xa2 && localNative.InCombat && localNative.Icon == 1,
        "B previews must use passive blue elite icons while retaining enemy battalion, elite shape and other flags.");
    localNative.GameObject.UpdateFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Unk2;
    Check(!NativeLocalPresentationCodec.Write(&localNative, 4, false)
        && localNative.GameObject.UpdateFlags == FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectUpdateFlags.Unk2,
        "Maintaining a passive B preview must not dirty its nameplate every frame.");
    Check(NativeLocalPresentationCodec.Write(&localNative, 4, true) && localNative.Flags == 0xa3
        && localNative.NameId == 0 && localNative.GameObject.BaseId == 0 && localNative.Health == 1,
        "B-to-A/S icon changes must preserve local identity and health.");
}

// Preview must never reach native playback for a stale, untracked or redrawing target.
var animationAccess = new FakeAnimationAccess();
var previewAccess = new FakeAccess();
previewAccess.Actors.Add(actor, original);
var previewSession = new SwapSession(previewAccess);
var preview = new ActionPreview(previewSession, animationAccess);
var testSkill = new SkillAnimation(27316, "巨魔碎颈臂", 3205, "mon_sp/[SKL_ID]/mon_sp005", 1);
{
    var a = new FakeAccess(); a.Actors[actor] = original;
    var s = new SwapSession(a); s.Apply(actor, "小怪", "布弗鲁", State(3315, 3)); s.Tick();
    var anim = new FakeAnimationAccess();
    var playback = new ActionPreview(s, anim);
    var paused = false; var owned = true;
    playback.IsPaused = _ => paused; playback.IsEligible = _ => owned;
    playback.Play(actor, testSkill, 0);
    Check(playback.IsBusy(actor, 749), "A manual animation must protect its startup frames from automatic stance changes.");
    anim.Active = true;
    Check(playback.IsBusy(actor, 20000), "A still-active native animation must retain priority beyond any fixed short timeout.");
    paused = true; playback.Tick(30000);
    Check(playback.IsBusy(actor, 100000), "Pause must retain an active manual animation's priority.");
    paused = false; playback.Tick(100001); anim.Active = false;
    Check(!playback.IsBusy(actor, 100002), "Once the native animation ends, automatic stance changes must be allowed again.");
    paused = true;
    var before = anim.Calls.Count;
    try { playback.Play(actor, testSkill, 101000); throw new Exception("Paused skill accepted."); }
    catch (InvalidOperationException) { Check(anim.Calls.Count == before, "A direct manual skill request must never replace a frozen pose."); }
    paused = false; playback.Play(actor, testSkill, 102000); playback.Tick(102100);
    paused = true; playback.Tick(102200); playback.Tick(200000);
    paused = false; playback.Tick(200001);
    Check(playback.IsBusy(actor, 200002), "Pause must preserve the startup grace period rather than consuming it in wall time.");
    anim.Active = true; playback.Stop(actor);
    Check(anim.Stopped.Single() == (actor, 3315, (ushort)3205) && !playback.IsBusy(actor, 200003),
        "Explicit reset must stop only the tracked manual timeline and release its priority.");
    playback.Play(actor, testSkill, 201000); owned = false;
    Check(!playback.IsBusy(actor, 201001), "A failed ownership check must discard manual priority without following a recycled actor.");
    playback.Stop(actor);
    Check(anim.Stopped.Count == 1, "A lost lease must not receive native stop calls.");
    owned = true; playback.Play(actor, testSkill, 202000);
    s.Apply(actor, "小怪", "另一模型", State(235, 1)); s.Tick();
    Check(!playback.IsBusy(actor, 202001), "A model change must not inherit the previous model's active skill lease.");
    playback.Clear();
    Check(!playback.IsBusy(actor, 202002), "Scene clearing must drop all animation priority state.");
    s.Apply(actor, "小怪", "布弗鲁", State(3315, 3)); s.Tick();
    playback.Play(actor, testSkill, 203000); anim.Active = true;
    playback.Forget(actor);
    Check(!playback.IsBusy(actor, 203001) && anim.Stopped.Count == 1,
        "Deleting an owned visual must drop its manual priority before the same identity can be reused, without stopping a new actor.");
}
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

var pauseActors = new FakeAccess();
pauseActors.Actors[actor] = original;
pauseActors.Actors[other] = original;
var pauseSession = new SwapSession(pauseActors);
var pauseAccess = new FakePauseAccess(pauseActors);
var clock = new AnimationClockState(0.8f, Enumerable.Range(0, 14).Select(i => 0.5f + i * 0.1f).ToArray());
pauseAccess.Clocks[actor] = clock;
pauseAccess.Clocks[other] = clock with { OverallSpeed = 1.25f };
var pauseEligible = true;
var pause = new AnimationPause(pauseSession, pauseAccess, _ => pauseEligible);
try { pause.Pause(actor); throw new Exception("Untracked pause accepted."); } catch (InvalidOperationException) { tests++; }
pauseSession.Apply(actor, "小怪", "布弗鲁", State(3315, 3));
try { pause.Pause(actor); throw new Exception("Redrawing pause accepted."); } catch (InvalidOperationException) { tests++; }
pauseSession.Tick();
pause.Pause(actor);
Check(pause.IsPaused(actor) && pause.IsPausedAddress(actor.Address) && !pause.IsPausedAddress(other.Address)
    && pauseAccess.Clocks[actor].OverallSpeed == 0 && pauseAccess.Clocks[actor].SlotSpeeds.All(s => s == 0),
    "Pause must freeze all fourteen slots and overall speed only for the tracked actor.");
pause.Pause(actor);
pauseAccess.Clocks[actor] = clock;
pause.Tick();
Check(pauseAccess.Clocks[actor].OverallSpeed == 0 && pauseAccess.Clocks[actor].SlotSpeeds.All(s => s == 0),
    "Game speed refreshes must not release a still-paused actor.");
Check(pause.Resume(actor) && !pause.IsPaused(actor) && pauseAccess.Clocks[actor].OverallSpeed == clock.OverallSpeed
    && pauseAccess.Clocks[actor].SlotSpeeds.SequenceEqual(clock.SlotSpeeds),
    "Resume must restore the first snapshot's distinct speeds instead of replacing them with 1 or repeated-pause zeros.");
Check(!pause.Resume(actor), "Resuming a non-paused actor must be a no-op.");
pauseSession.Apply(other, "另一只", "布弗鲁", State(3315, 3));
pauseSession.Tick();
pause.Pause(actor); pause.Pause(other);
pauseAccess.FailRestore = actor;
try { pause.Reset(); throw new Exception("Expected pause restoration failure."); } catch (AggregateException) { tests++; }
Check(pause.Actors.SequenceEqual(new[] { actor }) && pauseAccess.Clocks[other].OverallSpeed == 1.25f,
    "One restore failure must preserve its retry snapshot while allowing other actors to resume.");
pauseAccess.FailRestore = null;
pause.Reset();
Check(pause.Actors.Count == 0 && pauseAccess.Clocks[actor].SlotSpeeds.SequenceEqual(clock.SlotSpeeds),
    "Saved animation speeds must be recoverable after a transient restore failure.");
pauseAccess.FailFreeze = true;
try { pause.Pause(actor); throw new Exception("Expected partial freeze failure."); } catch (InvalidOperationException) { tests++; }
var partialFreezeWrites = pauseAccess.Writes;
pause.Tick(); pause.Tick();
Check(pauseAccess.Writes == partialFreezeWrites, "A failed native freeze must not be retried and logged every frame before the user resumes.");
Check(pause.IsPaused(actor) && pause.Resume(actor) && pauseAccess.Clocks[actor].OverallSpeed == 0.8f,
    "A partially failed pause must retain the original speeds so it can be resumed.");
pauseAccess.FailFreeze = false;
pause.Pause(actor);
pauseSession.Resize(actor, 5);
Check(pause.IsPaused(actor), "Resizing a model without redrawing it must preserve its paused state.");
pause.Resume(actor);
pause.Pause(actor);
pauseEligible = false;
var pauseWrites = pauseAccess.Writes;
Check(!pause.IsPausedAddress(actor.Address), "A lost local ownership marker must immediately stop native speed interception.");
pause.Tick();
Check(pause.Actors.Count == 0 && pauseAccess.Writes == pauseWrites,
    "Lost ownership must discard pause state without restoring into an unrelated actor.");
pauseEligible = true;
pauseAccess.Clocks[actor] = clock;
pause.Pause(actor);
pauseActors.Actors.Remove(actor);
pauseActors.Actors[reused] = original;
pauseWrites = pauseAccess.Writes;
pause.Tick();
Check(!pause.IsPausedAddress(reused.Address) && pause.Actors.Count == 0 && pauseAccess.Writes == pauseWrites,
    "Object-table slot reuse must not inherit paused animation speed or an old restoration.");
pauseActors.Actors[actor] = State(3315, 3);
pauseAccess.Clocks[actor] = clock;
pause.Pause(actor);
pauseWrites = pauseAccess.Writes;
pause.ForgetScene();
pause.Tick();
Check(pause.Actors.Count == 0 && pauseAccess.Writes == pauseWrites,
    "Scene cleanup must forget native animation leases without touching old scene memory.");
pauseAccess.Clocks[actor] = clock with { SlotSpeeds = new float[13] };
try { pause.Pause(actor); throw new Exception("Invalid clock accepted."); } catch (InvalidOperationException) { tests++; }
Check(pause.Actors.Count == 0 && pauseAccess.Writes == pauseWrites, "Invalid speed data must be rejected before any freeze write.");
unsafe
{
    NativeCharacter clockNative = default;
    clockNative.NameId = 321;
    clockNative.GameObject.Position = new(1, 2, 3);
    var clockPointer = &clockNative;
    var dirtySlots = new HashSet<uint>();
    void NativeSpeedSetter(uint slot, float speed)
    {
        // Matches the observed native behavior: identical cached speeds do not
        // set the scheduler's dirty flag, so render playback is not refreshed.
        if (clockPointer->Timeline.TimelineSequencer.TimelineSpeeds[(int)slot] == speed) return;
        clockPointer->Timeline.TimelineSequencer.TimelineSpeeds[(int)slot] = speed;
        dirtySlots.Add(slot);
    }
    NativeAnimationClockCodec.Write(&clockNative, clock, NativeSpeedSetter);
    var capturedClock = NativeAnimationClockCodec.Capture(&clockNative);
    NativeAnimationClockCodec.Write(&clockNative, new(0, new float[14]), NativeSpeedSetter);
    Check(clockNative.Timeline.OverallSpeed == 0 && clockNative.Timeline.TimelineSequencer.TimelineSpeeds.ToArray().All(s => s == 0),
        "Native clock writes must cover every animation slot.");
    dirtySlots.Clear();
    NativeAnimationClockCodec.Write(&clockNative, capturedClock, NativeSpeedSetter);
    Check(dirtySlots.Count == 14,
        "Resume must dirty every native slot, not prefill the cache and make SetSlotSpeed skip the scheduler refresh.");
    Check(clockNative.Timeline.OverallSpeed == 0.8f
        && clockNative.Timeline.TimelineSequencer.TimelineSpeeds.ToArray().SequenceEqual(clock.SlotSpeeds)
        && clockNative.NameId == 321 && clockNative.GameObject.Position.Y == 2,
        "Clock capture/restore must round-trip exact speeds without changing identity or world position.");
    dirtySlots.Clear();
    NativeAnimationClockCodec.Write(&clockNative, capturedClock, NativeSpeedSetter);
    Check(dirtySlots.Count == 14 && clockNative.Timeline.TimelineSequencer.TimelineSpeeds.ToArray().SequenceEqual(clock.SlotSpeeds),
        "An explicit repair with already-restored cache values must still refresh frozen render playback without changing final speeds.");
    try { NativeAnimationClockCodec.Write(&clockNative, new(float.NaN, new float[14]), NativeSpeedSetter); throw new Exception("Invalid clock write."); }
    catch (InvalidOperationException) { Check(clockNative.Timeline.OverallSpeed == 0.8f, "Invalid clock data must not partially overwrite the native state."); }
}
pauseAccess.Clocks[actor] = new(0, new float[14]);
Check(pause.Resume(actor, repairIfNotPaused: true) && pauseAccess.Clocks[actor].OverallSpeed == 1
    && pauseAccess.Clocks[actor].SlotSpeeds.All(s => s == 1),
    "Explicit resume without a pause snapshot must recover zero clocks left by an older version.");
Check(pause.Resume(actor, repairIfNotPaused: true) && pauseAccess.Clocks[actor].SlotSpeeds.All(s => s == 1),
    "A second explicit resume must remain usable and refresh existing playback.");
var rotationAnimations = new FakeAnimationAccess();
var rotationEligible = true;
var rotation = new ModelRotation(pauseSession, rotationAnimations, _ => rotationEligible);
rotation.Set(actor, 90);
Check(MathF.Abs(rotationAnimations.Rotations[actor] - MathF.PI / 2) < 0.001f,
    "Manual rotation must convert freely entered degrees into the native horizontal angle.");
rotationAnimations.Rotations[actor] = 0;
rotation.Tick();
Check(MathF.Abs(rotationAnimations.Rotations[actor] - MathF.PI / 2) < 0.001f,
    "The chosen heading must persist after an ordinary client facing refresh.");
rotation.Set(actor, 450);
Check(MathF.Abs(rotationAnimations.Rotations[actor] - MathF.PI / 2) < 0.001f,
    "Angles beyond a full revolution must normalize without restricting the requested heading.");
rotation.Set(actor, -45);
Check(MathF.Abs(ModelRotation.Degrees(rotationAnimations.Rotations[actor]) - 315) < 0.001f,
    "Negative rotations must wrap into a usable 0-360 degree display.");
rotation.Release(actor);
var rotationWrites = rotationAnimations.FacingWrites;
rotation.Tick();
Check(rotationAnimations.FacingWrites == rotationWrites && rotation.Actors.Count == 0,
    "Releasing manual heading must stop further writes without moving the model again.");
rotation.Set(actor, 180);
rotationEligible = false;
rotationWrites = rotationAnimations.FacingWrites;
rotation.Tick();
Check(rotation.Actors.Count == 0 && rotationAnimations.FacingWrites == rotationWrites,
    "Losing local ownership must discard rotation control without writing to the old actor.");
rotationEligible = true;
rotation.Set(actor, 0);
rotation.Clear();
rotationWrites = rotationAnimations.FacingWrites;
rotation.Tick();
Check(rotationAnimations.FacingWrites == rotationWrites, "Scene cleanup must discard rotation without native writes.");
try { rotation.Set(actor, float.NaN); throw new Exception("Invalid rotation accepted."); } catch (InvalidOperationException) { tests++; }
Check(rotationAnimations.FacingWrites == rotationWrites, "Invalid angles must not affect model orientation.");
pause.Pause(actor);
rotation.Set(actor, 270);
Check(pause.IsPaused(actor) && pauseAccess.Clocks[actor].SlotSpeeds.All(s => s == 0)
    && MathF.Abs(ModelRotation.Degrees(rotationAnimations.Rotations[actor]) - 270) < 0.001f,
    "A frozen model must remain paused while the independent manual rotation control changes its heading.");
pause.Resume(actor); rotation.Release(actor);
var pausedComboActors = new FakeAccess();
pausedComboActors.Actors[actor] = original;
var pausedComboSession = new SwapSession(pausedComboActors);
pausedComboSession.Apply(actor, "小怪", "布弗鲁", State(3315, 3)); pausedComboSession.Tick();
var pausedComboAnimations = new FakeAnimationAccess();
var pausedComboPlayback = new ActionPreview(pausedComboSession, pausedComboAnimations);
var isComboPaused = false;
var pausedCombo = new BufruSequence(pausedComboSession, pausedComboPlayback, pausedComboAnimations, isPaused: _ => isComboPaused);
pausedCombo.Start(actor, comboSkills, comboDirections, 1000, 0);
isComboPaused = true; pausedCombo.Tick(1000);
var beforePausedFacing = pausedComboAnimations.FacingWrites;
pausedCombo.Tick(11000);
Check(pausedCombo.RemainingSeconds(11000) == 3 && pausedComboAnimations.Calls.Count == 0
    && pausedComboAnimations.FacingWrites == beforePausedFacing,
    "Pausing a combo must freeze cast countdowns, facing and skill scheduling.");
isComboPaused = false; pausedCombo.Tick(11000); pausedCombo.Tick(13999);
Check(pausedComboAnimations.Calls.Count == 0, "Resume must preserve the remaining cast rather than catch up missed steps.");
pausedCombo.Tick(14000);
Check(pausedComboAnimations.Calls.Count == 1, "The paused cast must release only after its original remaining duration.");
isComboPaused = true; pausedCombo.Tick(14500); pausedCombo.Tick(24500);
Check(pausedCombo.RemainingSeconds(24500) == 0.5f && pausedComboAnimations.Calls.Count == 1,
    "Animation hold timing must pause as well as casting.");
isComboPaused = false; pausedCombo.Tick(24500); pausedCombo.Tick(25000); pausedCombo.Tick(25300);
Check(pausedComboAnimations.Calls.Count == 2, "The combo must continue the next recorded direction after resuming its hold.");
isComboPaused = true; pausedCombo.Tick(25400); pausedComboSession.ForgetDespawned();
pausedCombo.Tick(30000);
Check(!pausedCombo.Running, "Paused combos must still notice invalidated actor sessions and cancel.");

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
{
    var source = actor with { NameId = 541, DataId = 901 };
    var dummyState = State(2449, 1) with { NameBytes = DisplayName.Encode("木人"), Level = 1 };
    var deferCombat = false;
    (FakeAccess Actors, FakeLocalAccess Factory, SwapSession Session, LocalPreview Preview, FakeLinkedAccess Source, LinkedDummyPreview Link) Setup()
    {
        var a = new FakeAccess(); a.Actors.Add(source, dummyState);
        var factory = new FakeLocalAccess(a);
        var swaps = new SwapSession(a);
        var p = new LocalPreview(swaps, factory);
        var sourceAccess = new FakeLinkedAccess(a);
        return (a, factory, swaps, p, sourceAccess, new LinkedDummyPreview(swaps, p, a, sourceAccess, _ => deferCombat));
    }
    var f = Setup();
    f.Link.Start(source, "木人", sampleHunt, sampleHunt.Scale, 0);
    var visual = f.Link.Visual!.Value;
    Check(f.Factory.Placements.Single() == new LocalPlacement(new(10, 20, 30), 1.25f, true),
        "A furnishing replacement must spawn at the exact target position/rotation and be selectable for source redirection.");
    Check(f.Link.Replaces(source) && f.Preview.OwnsLive(visual) && visual.NameId == 0 && visual.DataId == 0
        && f.Session.Entries.Single(e => e.Actor == source).Original == dummyState,
        "The linked visual must retain nonce ownership and no real hunt identity, while saving the original training target.");
    var sourceEntry = f.Session.Entries.Single(e => e.Actor == source);
    Check(sourceEntry.PresentationOnly && !f.Actors.Hidden.Contains(source) && !f.Actors.Writes.Any(w => w.Actor == source),
        "Applying a furnishing swap must never DisableDraw or rebuild the real source, which would clear its IsDrawn bit.");
    Check(sourceEntry.Applied.ModelId == dummyState.ModelId && sourceEntry.Applied.ModelScale == dummyState.ModelScale
        && sourceEntry.Applied.ObjectScale == dummyState.ObjectScale && sourceEntry.Applied.DrawScale == dummyState.DrawScale,
        "The real training target must retain its original model and every scale; only the owned visual uses hunt geometry.");
    Check(f.Actors.Actors[source].NameBytes.SequenceEqual(DisplayName.Encode(sampleHunt.Name))
        && f.Actors.Actors[source].Level == sampleHunt.Level && f.Actors.Actors[source].NameplateIcon == sampleHunt.NameplateIcon
        && f.Actors.Actors[visual].ModelId == sampleHunt.ModelId,
        "The source target bar must receive the hunt name/level/icon while the proxy receives the actual hunt model.");
    var selection = new LinkedDummyTarget(f.Link, f.Preview, f.Actors);
    Check(selection.Redirect(visual.Address) == source.Address,
        "Selecting the actual owned furnishing visual must immediately resolve to the real training target.");
    Check(selection.Redirect(0) == 0 && selection.Redirect(source.Address) == source.Address
        && selection.Redirect(other.Address) == other.Address,
        "Clear-target, direct source selection and unrelated actor selection must remain unchanged.");
    f.Session.Tick(); f.Link.Tick(1);
    Check(!f.Actors.Hidden.Contains(source) && !f.Actors.Writes.Any(w => w.Actor == source),
        "Source upkeep must not disable/re-enable native drawing while the separate layout graphics are concealed.");
    try { f.Session.Resize(source, 8); throw new Exception("Expected presentation-only resize rejection."); }
    catch (InvalidOperationException) { Check(f.Actors.Actors[source].ModelScale == dummyState.ModelScale,
        "A source resize must be rejected so the real furnishing keeps its targeting geometry."); }
    f.Session.Resize(visual, 8);
    Check(f.Actors.Actors[visual].ModelScale == 8 && f.Actors.Actors[source].ModelScale == dummyState.ModelScale,
        "Free resizing must change only the actual linked visual, independently of the training target.");
    Check(f.Source.IsHidden(source) && f.Source.Aligned.Single() == (visual, source),
        "A live linked visual must conceal the original source and follow its position without changing the training identity.");
    f.Link.Clear();
    Check(selection.Redirect(visual.Address) == visual.Address,
        "Clearing a furnishing link must immediately stop redirecting its previous visual address.");
    Check(f.Factory.Deleted.Count == 1 && f.Link.Source is null && f.Source.Restores.Count == 0,
        "Removing a linked model must delete its owned visual and defer original visibility restoration until redraw finishes.");
    f.Session.Tick(); f.Link.Tick(2);
    Check(f.Actors.Actors[source] == dummyState && !f.Source.IsHidden(source) && f.Session.Entries.Count == 0,
        "Restoring a furnishing replacement must restore its original appearance and visibility without a remaining proxy.");
    Check(!f.Actors.Writes.Any(w => w.Actor == source),
        "Restoring the source's display fields must not perform a native source redraw either.");

    f = Setup(); f.Link.Start(source, "木人", sampleHunt, 3, 0);
    f.Link.Clear(); f.Session.Shutdown(); f.Link.FinishRestores();
    Check(f.Actors.Actors[source] == dummyState && !f.Actors.Writes.Any(w => w.Actor == source)
        && !f.Actors.Hidden.Contains(source) && !f.Source.IsHidden(source),
        "Unload between apply and the next tick must restore the real furnishing without ever toggling native draw readiness.");

    f = Setup();
    f.Link.Start(source, "木人", sampleHunt, 3, 0); f.Session.Tick();
    f.Link.Clear(); // Start another swap before the previous source restore has drawn.
    f.Link.Start(source, "木人", sampleHunt with { Name = "第二外观", ModelId = 235 }, 0.5f, 1);
    f.Session.Tick(); f.Link.Tick(2);
    Check(f.Source.IsHidden(source) && f.Session.Entries.Single(e => e.Actor == source).Original == dummyState,
        "A same-frame reapply must keep the original snapshot and must not unhide the old layout beneath the new visual.");
    f.Link.Clear(); f.Session.Tick(); f.Link.Tick(3);
    Check(!f.Source.IsHidden(source) && f.Actors.Actors[source] == dummyState,
        "Quick repeated furnishing swaps must not capture their own temporary hidden state as the original.");

    f = Setup(); f.Source.Hidden.Add(source);
    f.Link.Start(source, "木人", sampleHunt, 3, 0); f.Session.Tick();
    f.Link.Clear(); f.Session.Tick(); f.Link.Tick(2);
    Check(f.Source.IsHidden(source), "An originally hidden furnishing must remain hidden after linked visual removal.");

    f = Setup();
    f.Link.Start(source, "木人", sampleHunt, 3, 0); f.Session.Tick();
    f.Factory.FailDelete = true;
    try { f.Link.Clear(); throw new Exception("Expected linked delete failure."); } catch (InvalidOperationException) { }
    Check(f.Link.Source == source && f.Preview.OwnsLive(f.Link.Visual!.Value),
        "A failed proxy deletion must retain its complete link and nonce lease for retry.");
    f.Factory.FailDelete = false; f.Link.Clear(); f.Session.Tick(); f.Link.Tick(2);
    Check(!f.Source.IsHidden(source), "Retrying proxy removal must finish restoring the source.");

    f = Setup(); f.Source.Ready = false;
    f.Link.Start(source, "木人", sampleHunt, 3, 0); f.Session.Tick();
    try { f.Link.Tick(5000); throw new Exception("Expected proxy load timeout."); } catch (InvalidOperationException) { }
    f.Session.Tick(); f.Link.FinishRestores();
    Check(f.Link.Source is null && f.Preview.Actor is null && !f.Source.IsHidden(source),
        "A visual that never loads must roll back instead of reporting a successful replacement indefinitely.");

    f = Setup();
    f.Link.Start(source, "木人", sampleHunt, 3, 0); f.Session.Tick();
    var recycledSource = source with { ObjectId = source.ObjectId + 1 };
    var sourceLostSelection = new LinkedDummyTarget(f.Link, f.Preview, f.Actors);
    f.Actors.Actors.Remove(source); f.Actors.Actors.Add(recycledSource, original);
    Check(sourceLostSelection.Redirect(f.Link.Visual!.Value.Address) == 0,
        "An owned visual with a missing/recycled real source must not select the foreign actor at the old source address.");
    f.Link.Tick(1); f.Session.Tick(); f.Link.FinishRestores();
    Check(f.Preview.Actor is null && f.Actors.Actors[recycledSource] == original && f.Source.Restores.Count == 0,
        "A despawned source must remove the local visual without restoring anything into a recycled target slot.");

    f = Setup();
    f.Link.Start(source, "木人", sampleHunt, 3, 0); f.Session.Tick();
    var oldVisual = f.Link.Visual!.Value;
    f.Actors.Actors[oldVisual] = original; // Same identity/address, different native ownership marker.
    var nonceLostSelection = new LinkedDummyTarget(f.Link, f.Preview, f.Actors);
    Check(nonceLostSelection.Redirect(oldVisual.Address) == oldVisual.Address,
        "A foreign object reusing the visual's identity/address must never be redirected after its nonce changes.");
    f.Link.Tick(1); f.Session.Tick(); f.Link.FinishRestores();
    Check(f.Factory.Deleted.Count == 0 && f.Actors.Actors[oldVisual] == original && !f.Source.IsHidden(source),
        "A lost proxy nonce must restore the source but must never delete or rewrite the foreign object.");

    f = Setup(); f.Link.Start(source, "木人", sampleHunt, 3, 0);
    var linkedWriteCount = f.Actors.Writes.Count;
    f.Link.ForgetScene(); f.Preview.ForgetScene(); f.Session.ForgetDespawned(); f.Link.Tick(1);
    Check(f.Actors.Writes.Count == linkedWriteCount && f.Factory.Deleted.Count == 0 && f.Source.Forgotten,
        "A territory change must discard the link without touching native objects from the prior scene.");
    Check(new LinkedDummyTarget(f.Link, f.Preview, f.Actors).Redirect(source.Address) == source.Address,
        "A scene change must leave future unrelated targeting requests unmodified.");

    f = Setup();
    var ordinaryPreview = f.Preview.Spawn(sampleHunt, 3, 8);
    Check(new LinkedDummyTarget(f.Link, f.Preview, f.Actors).Redirect(ordinaryPreview.Address) == ordinaryPreview.Address,
        "An ordinary front-of-player preview must retain its original selection behavior without a furnishing association.");
    f.Preview.Clear();
    f.Link.Start(source, "木人", sampleHunt, 3, 0);
    var previousVisual = f.Link.Visual!.Value;
    var repeatSelection = new LinkedDummyTarget(f.Link, f.Preview, f.Actors);
    f.Link.Clear(); f.Link.Start(source, "木人", sampleHunt, 3, 1);
    Check(repeatSelection.Redirect(previousVisual.Address) == previousVisual.Address
        && repeatSelection.Redirect(f.Link.Visual!.Value.Address) == source.Address,
        "Repeated furnishing replacements must redirect only the current owned visual, never an old one.");

    f = Setup(); f.Link.Start(source, "木人", sampleHunt, 3, 0); f.Session.Tick();
    f.Link.Tick(1);
    Check(f.Source.CombatWrites.Single().Combat == CombatPresentation.From(false, false)
        && !f.Source.CombatWrites.Single().ChangePose,
        "A fresh idle proxy must not receive a needless animation restart.");
    f.Source.Combat = CombatPresentation.From(true, false); // A furnishing need not itself have a drawn weapon.
    f.Link.Tick(2);
    Check(f.Source.CombatWrites[^1].Combat == new CombatPresentation(true, true)
        && f.Source.CombatWrites[^1].ChangePose && f.Source.CombatWrites[^1].Visual == f.Preview.Actor,
        "A real source entering combat must put its current owned visual into battle stance even without a weapon or HP delta.");
    f.Link.Tick(3);
    Check(!f.Source.CombatWrites[^1].ChangePose && f.Source.CombatWrites.Count(w => w.ChangePose) == 1,
        "Steady combat must maintain flags without replaying idle every frame and interrupting actions.");
    f.Source.Combat = CombatPresentation.From(false, false); f.Link.Tick(4);
    Check(f.Source.CombatWrites[^1].ChangePose && !f.Source.CombatWrites[^1].Combat.WeaponDrawn,
        "Source disengagement must return the visual to normal idle once.");
    deferCombat = true;
    var deferredCount = f.Source.CombatWrites.Count;
    f.Source.Combat = CombatPresentation.From(true, false); f.Link.Tick(5);
    Check(f.Source.CombatWrites.Count == deferredCount,
        "Paused models and a running manual combo must defer combat flag writes and pose transitions entirely.");
    deferCombat = false; f.Link.Tick(6);
    Check(f.Source.CombatWrites[^1].ChangePose && f.Source.CombatWrites[^1].Combat.InCombat,
        "Resume must apply the latest battle state without replaying a backlog.");
    deferCombat = true;
    deferredCount = f.Source.CombatWrites.Count;
    f.Source.Combat = CombatPresentation.From(false, false); f.Link.Tick(7);
    f.Source.Combat = CombatPresentation.From(true, false); f.Link.Tick(8);
    deferCombat = false; f.Link.Tick(9);
    Check(f.Source.CombatWrites.Count == deferredCount + 1 && !f.Source.CombatWrites[^1].ChangePose,
        "A transient disengage/reengage while paused must not unnecessarily replace the saved pose on resume.");
    f.Source.Ready = false; deferredCount = f.Source.CombatWrites.Count; f.Link.Tick(10);
    Check(f.Source.CombatWrites.Count == deferredCount,
        "An unloaded or rebuilding model must not receive timeline calls.");
    f.Source.Ready = true; f.Link.Tick(11);
    Check(f.Source.CombatWrites[^1].ChangePose && f.Source.CombatWrites[^1].Combat.InCombat,
        "After model reload the visual must regain its current battle pose.");
    f.Link.Clear(); deferredCount = f.Source.CombatWrites.Count; f.Session.Tick(); f.Link.Tick(12);
    Check(f.Source.CombatWrites.Count == deferredCount,
        "Restoring a furnishing must stop writing combat state into the deleted local actor.");
    f.Link.Start(source, "木人", sampleHunt, 3, 13); f.Session.Tick(); f.Link.Tick(14);
    Check(f.Source.CombatWrites[^1].ChangePose,
        "Reapplying while already in combat must initialize the new model's stance.");
    deferredCount = f.Source.CombatWrites.Count;
    f.Actors.Actors[f.Link.Visual!.Value] = original; f.Link.Tick(15);
    Check(f.Source.CombatWrites.Count == deferredCount,
        "A lost nonce must prevent every combat-state and animation write to a recycled visual.");

    f = Setup();
    var front = f.Preview.Spawn(sampleHunt, 2, 8); f.Session.Tick();
    f.Link.Pose.SetMode(ModelStance.Battle); f.Link.Tick(1);
    Check(f.Link.Source is null && f.Source.CombatWrites.Single().Visual.Actor == front
        && f.Source.CombatWrites.Single().Combat == new CombatPresentation(true, true),
        "Forced battle stance must also work for an ordinary front preview with no training source.");
    f.Link.Pose.SetMode(ModelStance.Idle); deferCombat = true; f.Link.Tick(2);
    Check(f.Source.CombatWrites.Count == 1, "Changing the stance choice while paused must not replace the frozen animation.");
    deferCombat = false; f.Link.Tick(3);
    Check(f.Source.CombatWrites[^1].ChangePose && f.Source.CombatWrites[^1].Combat == default,
        "Resuming must apply the latest selected normal stance to the same local actor.");
    f.Link.Pose.Reset(); f.Link.Tick(4);
    Check(f.Preview.Actor!.Actor == front && f.Link.Pose.Mode == ModelStance.Automatic && f.Source.CombatWrites[^1].ChangePose,
        "Resetting stance must preserve the actor and reapply automatic idle exactly once.");
    f.Preview.Clear(); deferredCount = f.Source.CombatWrites.Count; f.Link.Tick(5);
    Check(f.Source.CombatWrites.Count == deferredCount, "Removing a front preview must stop all stance writes.");
}

{
    var pose = new CombatPose();
    var lease = new LocalActorLease(100, 0x5000, actor, 1);
    var writes = new List<(CombatPresentation Combat, bool Change)>();
    void Write(LocalActorLease _, CombatPresentation combat, bool change) => writes.Add((combat, change));
    pose.Tick(lease, 3315, default, true, false, Write);
    pose.SetMode(ModelStance.Battle); pose.Tick(lease, 3315, default, true, false, Write);
    pose.Tick(lease, 3315, default, true, false, Write);
    Check(writes[1] == (new CombatPresentation(true, true), true) && !writes[2].Change,
        "Forced combat must ignore an idle source and must not restart the pose during stable frames.");
    pose.SetMode(ModelStance.Idle); pose.Tick(lease, 3315, new(true, true), true, false, Write);
    Check(writes[^1] == (default(CombatPresentation), true), "Forced idle must override a fighting source without changing that source.");
    pose.SetMode(ModelStance.Automatic); pose.Tick(lease, 3315, new(true, true), true, true, Write);
    var count = writes.Count;
    pose.Tick(lease, 3315, new(false, false), true, false, Write);
    Check(writes.Count == count + 1 && !writes[^1].Combat.WeaponDrawn,
        "Automatic mode must sample the latest source after a priority delay, ignoring obsolete intermediate states.");
    pose.SetMode(ModelStance.Battle); count = writes.Count;
    pose.Tick(lease, 3315, default, false, false, Write);
    Check(writes.Count == count, "Loading must defer explicit stance requests too.");
    pose.Tick(lease, 3315, default, true, false, Write);
    pose.Tick(lease with { Token = 2 }, 3315, default, true, false, Write);
    Check(writes[^1].Change, "A new ownership lease must initialize its own pose rather than inherit the old cache.");
    pose.Tick(lease with { Token = 2 }, 235, default, true, false, Write);
    Check(writes[^1].Change, "A new model sharing the same owned actor must initialize its new pose.");
    try { pose.SetMode((ModelStance)99); throw new Exception("Invalid stance accepted."); }
    catch (InvalidOperationException) { Check(pose.Mode == ModelStance.Battle, "Invalid saved stance choices must not replace the last valid mode."); }
    pose.SetMode(ModelStance.Idle);
    try { pose.Tick(lease, 235, default, true, false, (_, _, _) => throw new InvalidOperationException("Write failed")); }
    catch (InvalidOperationException) { }
    pose.Tick(lease, 235, default, true, false, Write);
    Check(writes[^1].Change, "A failed native pose write must retain the pending transition for retry.");
}

{
    var defaults = new DemoDefaults();
    defaults.Remember(actor, 3315, 45, 3);
    defaults.Remember(actor, 3315, 180, 2.5f);
    Check(defaults.For(actor, 3315) == new DemoDefault(3315, 45, 2.5f),
        "Reset defaults must retain the initial heading but update native scale when another appearance shares the model.");
    Check(defaults.For(reused, 3315) is null && defaults.For(actor, 235) is null,
        "Recycled actors and different models must not receive another model's reset defaults.");
    defaults.Remember(actor, 235, 90, 0.5f);
    Check(defaults.For(actor, 235) == new DemoDefault(235, 90, 0.5f), "A changed model must establish its own reset defaults.");
    defaults.Prune([new SwapEntry(actor, "original", "demo", original, State(235, 2), false, true)]);
    Check(defaults.For(actor, 235) is null, "Restoring actors must lose their reset defaults.");
    defaults.Remember(actor, 235, 90, 0.5f); defaults.Clear();
    Check(defaults.For(actor, 235) is null, "Scene changes must forget reset defaults without native writes.");
    defaults.Remember(actor, 235, 90, 0.5f); defaults.Forget(actor); defaults.Remember(actor, 235, 270, 0.5f);
    Check(defaults.For(actor, 235)!.Heading == 270,
        "A new local lease reusing the exact identity and model must get its new initial heading after deletion cleanup.");
}

{
    var raw = new UserPreferences { SelectedNameId = 10617, SelectedBaseId = 13834, Rank = 999, Expansion = -99,
        Search = new string('测', 200), SpawnDistance = float.NaN, Stance = (ModelStance)99,
        Scales = new() { ["10617:13834"] = 12.75f, ["bad"] = 5, ["1:2"] = float.PositiveInfinity, ["3:4"] = -1 },
        Sections = new() { ["modelControls"] = true, ["hiddenTargets"] = false, ["unknown"] = true } };
    var safe = raw.Normalize();
    Check(safe.Rank == 0 && safe.Expansion == -1 && safe.Search.Length == 128 && safe.SpawnDistance == 8
        && safe.Stance == ModelStance.Automatic, "Invalid persisted choices must fall back to usable defaults.");
    Check(safe.Scales.Count == 1 && safe.Scales[UserPreferences.HuntKey(10617, 13834)] == 12.75f,
        "Per-hunt preferences must preserve free valid scales and discard malformed IDs or invalid floats.");
    Check(safe.Sections.Count == 2 && safe.Sections["modelControls"] && !safe.Sections["hiddenTargets"],
        "Remembered sections must retain known collapse states and reject unknown keys.");
    var json = System.Text.Json.JsonSerializer.Serialize(safe);
    var restoredPreferences = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>(json)!.Normalize();
    Check(restoredPreferences.SelectedNameId == 10617 && restoredPreferences.SelectedBaseId == 13834
        && restoredPreferences.Scales["10617:13834"] == 12.75f && restoredPreferences.Sections["modelControls"],
        "Settings must round-trip through actual JSON using stable hunt IDs rather than catalog indices.");
    var legacy = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>("{}")!.Normalize();
    Check(legacy.SpawnDistance == 8 && legacy.Expansion == -1 && legacy.Scales.Count == 0,
        "Existing configurations without the new preference fields must load safely.");
    var nullCollections = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>("{\"Search\":null,\"Scales\":null,\"Sections\":null}")!.Normalize();
    Check(nullCollections.Search == "" && nullCollections.Scales.Count == 0 && nullCollections.Sections.Count == 0,
        "Explicit null values from damaged or edited JSON must normalize safely.");
    Check(!json.Contains("Address") && !json.Contains("ActorIdentity") && !json.Contains("Token"),
        "Preference persistence must not retain scene actors, native pointers or ownership nonces.");
}

{
    var gate = new RefreshGate(250);
    Check(gate.Due(1000, 1) && !gate.Due(1001, 1) && !gate.Due(1249, 1) && gate.Due(1250, 1),
        "Stable bulk scans must be throttled while preserving the documented refresh boundary.");
    Check(gate.Due(1251, 2), "A replacement membership change must immediately invalidate bulk candidates.");
    gate.Reset(); Check(gate.Due(1252, 2), "A scene or mode change must force an immediate fresh scan.");
    var a = new FakeAccess(); a.Actors[actor] = original;
    var s = new SwapSession(a); var empty = s.Snapshot;
    Check(ReferenceEquals(empty, s.Snapshot), "An unchanged empty session must reuse its snapshot.");
    s.Apply(actor, "original", "demo", State(3315, 3)); var first = s.Snapshot; s.Tick();
    var drawn = s.Snapshot;
    Check(!ReferenceEquals(first, drawn) && !drawn[0].WaitingForDraw,
        "Completing redraw must publish a new entry snapshot.");
    var revision = s.Revision; s.Tick();
    Check(ReferenceEquals(drawn, s.Snapshot) && s.Revision == revision,
        "Ordinary presentation upkeep must not rebuild the session snapshot every frame.");
    s.Resize(actor, 5);
    Check(!ReferenceEquals(drawn, s.Snapshot) && s.Snapshot[0].Applied.ModelScale == 5 && drawn[0].Applied.ModelScale == 3,
        "A resize must invalidate the cache without mutating snapshots already read by the UI.");
    s.Restore(actor); Check(s.Snapshot[0].Restoring, "A restore request must immediately invalidate cached row state.");
    s.Tick(); Check(s.Snapshot.Length == 0, "Finishing restore must remove cached entries.");
    s.Apply(actor, "original", "demo", State(3315, 3)); a.Actors.Remove(actor); s.Tick();
    Check(s.Snapshot.Length == 0, "Losing an actor must invalidate the snapshot even without a UI request.");
}

unsafe
{
    NativeCharacter combatVisual = default;
    combatVisual.Flags = 0xA1;
    combatVisual.Timeline.Flags3 = 0x95;
    combatVisual.NameId = 101; combatVisual.GameObject.BaseId = 202;
    combatVisual.GameObject.OwnerId = 303; combatVisual.Health = 44; combatVisual.MaxHealth = 44;
    combatVisual.Battalion = 4; combatVisual.Timeline.BaseOverride = 321;
    NativeCombatPresentationCodec.Write(&combatVisual, CombatPresentation.From(true, false));
    Check(combatVisual.InCombat && combatVisual.Timeline.IsWeaponDrawn && combatVisual.Flags == 0xA3
        && combatVisual.Timeline.Flags3 == 0xD5,
        "Combat presentation must alter only the two documented combat/weapon bits, preserving all other flags.");
    Check(combatVisual.NameId == 101 && combatVisual.GameObject.BaseId == 202 && combatVisual.GameObject.OwnerId == 303
        && combatVisual.Health == 44 && combatVisual.MaxHealth == 44 && combatVisual.Battalion == 4
        && combatVisual.Timeline.BaseOverride == 321,
        "Battle stance must not change HP, real identity, ownership, aggression class or force an animation lock.");
    NativeCombatPresentationCodec.Write(&combatVisual, CombatPresentation.From(false, false));
    Check(!combatVisual.InCombat && !combatVisual.Timeline.IsWeaponDrawn && combatVisual.Flags == 0xA1
        && combatVisual.Timeline.Flags3 == 0x95,
        "Disengagement must clear only the combat/weapon bits and preserve unrelated native state.");
    Check(CombatPresentation.From(false, true) == new CombatPresentation(false, true),
        "An independently drawn source must retain battle stance without fabricating an InCombat state.");
}
{
    HuntAppearance[] iconCatalog = [sampleHunt, sampleHunt with { Rank = 1, Name = "B怪" },
        sampleHunt with { Rank = 2, Name = "A怪" }];
    var state = State(sampleHunt.ModelId, 1) with { NameplateIcon = 1 };
    var entry = new SwapEntry(actor, "主动普通小怪", "B怪", original, state, false, false);
    Check(NameplateIconPolicy.Resolve(entry, iconCatalog) == 61704,
        "A real aggressive source replaced with B must display the native blue elite resource.");
    Check(NameplateIconPolicy.Resolve(entry with { OriginalName = "被动普通小怪", ReplacementName = "A怪" }, iconCatalog) == 61710,
        "A passive source replaced with A must display its aggressive elite resource, independently of source classification.");
    Check(NameplateIconPolicy.Resolve(entry with { ReplacementName = sampleHunt.Name }, iconCatalog) == 61710,
        "S replacements must select the target's native elite icon rather than inherit a passive source icon.");
    Check(NameplateIconPolicy.Resolve(entry with { Restoring = true }, iconCatalog) is null
        && NameplateIconPolicy.Resolve(entry with { WaitingForDraw = true }, iconCatalog) is null
        && NameplateIconPolicy.Resolve(null, iconCatalog) is null,
        "Restore, redraw and untracked plates must return to the game's own icon calculation.");
    Check(NameplateIconPolicy.Resolve(entry with { ReplacementName = "未知" }, iconCatalog) is null
        && NameplateIconPolicy.Resolve(entry with { Applied = state with { ModelId = -1 } }, iconCatalog) is null
        && NameplateIconPolicy.Resolve(entry with { Applied = state with { NameplateIcon = 2 } }, iconCatalog) is null,
        "The icon override must validate target name, model and native icon category.");
    Check(NameplateIconPolicy.Resolve(entry, [iconCatalog[1] with { NameplateIcon = 2 }]) is null,
        "Unknown future icon shapes must not silently use today's elite resources.");
    unsafe
    {
        NativeCharacter native = default;
        native.Flags = 0xa3;
        native.Battalion = 4;
        native.NameId = 999;
        native.GameObject.BaseId = 888;
        native.Health = native.MaxHealth = 123;
        NativeAppearanceCodec.Write(&native, state);
        Check(native.Flags == 0xa3 && native.IsHostile && native.Battalion == 4 && native.NameId == 999
            && native.GameObject.BaseId == 888 && native.Health == 123,
            "Replacing a real actor's presentation must preserve its aggression, combat bits, battalion and battle identity.");
        native.IsHostile = false;
        NativeAppearanceCodec.Write(&native, state);
        Check(!native.IsHostile && native.Flags == 0xa2,
            "An aggressive target icon must not change a passive source actor's actual aggression bit.");
    }
}
{
    var passiveHunt = sampleHunt with { Rank = 1, Name = "被动B" };
    HuntAppearance[] appearances = [sampleHunt, passiveHunt];
    var iconAccess = new FakeAccess();
    var iconSession = new SwapSession(iconAccess);
    var iconFactory = new FakeLocalAccess(iconAccess);
    var iconPreview = new LocalPreview(iconSession, iconFactory, e =>
        appearances.FirstOrDefault(h => h.ModelId == e.Applied.ModelId && h.Name == e.ReplacementName)?.IsAggressive);
    var iconActor = iconPreview.Spawn(passiveHunt, 1, 8);
    iconSession.Tick(); iconPreview.Tick();
    Check(!iconPreview.Actor!.IsAggressive && !iconFactory.Refreshed[^1].IsAggressive,
        "A freshly spawned B preview must maintain passive classification on subsequent frames.");
    iconSession.Apply(iconActor, "local", sampleHunt.Name, iconPreview.PreserveOwnership(iconActor, State(sampleHunt.ModelId, 1)));
    iconSession.Tick(); iconPreview.Tick();
    Check(iconPreview.Actor!.IsAggressive && iconFactory.Refreshed[^1].IsAggressive,
        "Replacing a spawned B with S must switch the maintained icon classification.");
    iconSession.Apply(iconActor, "local", passiveHunt.Name, iconPreview.PreserveOwnership(iconActor, State(passiveHunt.ModelId, 1)));
    iconSession.Tick(); iconPreview.Tick();
    Check(!iconPreview.Actor!.IsAggressive && !iconFactory.Refreshed[^1].IsAggressive,
        "Replacing a spawned S with B must restore the blue passive classification even with an identical model ID.");
    iconPreview.Clear();
    Check(iconPreview.Actor is null && iconFactory.Deleted.Count == 1,
        "Changing presentation metadata must preserve ownership checks and native actor deletion.");
}
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

var bulkAccess = new FakeVisibilityAccess();
bulkAccess.Flags.Add(actor, false);
bulkAccess.Flags.Add(other, false);
bulkAccess.Flags.Add(reused, true);
var bulk = new ModelVisibility(bulkAccess);
bulk.SetHideAllMonsters(true);
bulk.Tick(actor, notRedrawing, new[] { actor, other });
Check(bulk.HideAllMonsters && bulk.Active && !bulkAccess.Flags[actor] && bulkAccess.Flags[other],
    "Bulk hiding must hide supplied monsters while keeping the independent self switch off.");
bulk.Tick(actor, notRedrawing, new[] { other, reused });
Check(bulkAccess.Flags[reused] && bulk.Count == 2,
    "Monsters newly entering range must join bulk hiding and preserve their original visibility.");
bulk.Show(other);
bulk.Tick(actor, notRedrawing, new[] { other, reused });
bulk.Tick(actor, notRedrawing, new[] { other, reused });
Check(!bulkAccess.Flags[other] && bulkAccess.Flags[reused] && bulk.HideAllMonsters,
    "Individually restoring one monster during bulk mode must not hide it again next frame.");
bulk.Hide(other);
bulk.Tick(actor, notRedrawing, new[] { other, reused });
Check(bulkAccess.Flags[other], "An explicit hide must override that monster's bulk-mode show exception.");
bulk.SetHideAllMonsters(false);
bulk.Tick(actor, notRedrawing);
Check(bulkAccess.Flags[other] && bulkAccess.Flags[reused] && bulk.Count == 1,
    "Disabling bulk mode must preserve individual hides and restore pre-existing hidden state.");
bulk.Show(other);
bulk.Tick(actor, notRedrawing);
Check(!bulkAccess.Flags[other] && !bulk.Active, "Individual restoration must also work after bulk mode is disabled.");
bulk.SetHideAllMonsters(true);
bulk.Tick(actor, new HashSet<ActorIdentity> { other }, new[] { other });
Check(!bulkAccess.Flags[other] && bulk.Count == 0,
    "Bulk hiding must not capture temporary DisableDraw state during a swap.");
bulk.Tick(actor, notRedrawing, new[] { other });
Check(bulkAccess.Flags[other] && bulk.Count == 1,
    "A redraw must finish before bulk hiding records and applies the actual visibility.");
bulk.Tick(actor, notRedrawing, Array.Empty<ActorIdentity>());
Check(!bulkAccess.Flags[other] && bulk.Count == 0 && bulk.HideAllMonsters,
    "A monster becoming a protected demo must be restored when removed from bulk candidates.");
bulk.Show(other);
bulk.SetHideAllMonsters(false);
bulk.SetHideAllMonsters(true);
bulk.Tick(actor, notRedrawing, new[] { other });
Check(bulkAccess.Flags[other], "Re-enabling bulk mode must reset its previous per-monster show exceptions.");
bulkAccess.Flags.Remove(other);
bulkAccess.Flags.Add(newOwner, false);
bulk.Tick(actor, notRedrawing, new[] { other });
Check(!bulkAccess.Flags[newOwner] && !bulk.ManagedActors.Contains(other),
    "A stale bulk identity must never hide or restore a recycled object-table slot.");
bulk.SetHideSelf(true);
bulk.Tick(actor, notRedrawing, new[] { newOwner, reused });
Check(bulkAccess.Flags[actor] && bulkAccess.Flags[newOwner], "Bulk and self hiding must operate together.");
bulk.Reset();
Check(!bulk.HideAllMonsters && !bulk.HideSelf && !bulk.Active && bulk.Count == 0
    && !bulkAccess.Flags[actor] && !bulkAccess.Flags[newOwner] && bulkAccess.Flags[reused],
    "Show-all must disable both switches, clear overrides and restore exact original visibility.");
bulk.SetHideAllMonsters(true);
bulk.Show(newOwner);
var bulkWrites = bulkAccess.Writes;
bulk.ForgetScene();
bulk.Tick(actor, notRedrawing, new[] { newOwner });
Check(!bulk.HideAllMonsters && !bulk.Active && bulkAccess.Writes == bulkWrites,
    "Changing scenes must discard bulk mode and its pending exceptions without touching new scene objects.");
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

// Dialogue timers must survive target selection changes and stop before touching stale models.
{
    var a = new FakeAccess();
    a.Actors[actor] = State(17, 1);
    var s = new SwapSession(a);
    s.Apply(actor, "original", "speaker", State(18, 2));
    s.Tick();
    var native = new FakeDialogueAccess();
    var speechEligible = true;
    var talk = new DialoguePreview(s, native, _ => speechEligible);
    var steps = new[] { new DialogueStep("first\nline", 5, 6000), new DialogueStep("second", 3, 3000),
        new DialogueStep("third", 3, 4000), new DialogueStep("fourth", 3, 1000) };
    talk.Start(actor, steps, 1000);
    Check(native.Opens.Count == 1 && native.Opens[0].Text == "first\nline" && talk.Running, "First native bubble opens immediately, preserving newlines.");
    talk.Tick(6999);
    Check(native.Opens.Count == 1, "Speech must not advance before its deadline.");
    talk.Tick(7000); talk.Tick(10000); talk.Tick(14000);
    Check(native.Opens.Select(o => o.Text).SequenceEqual(steps.Select(s => s.Text)) && !talk.Running,
        "Four-line playback must use the requested 0/6/9/13-second cadence.");
    talk.Clear(actor);
    Check(native.Closes.Single() == (actor, 18, "fourth"), "Clear must only close the last bubble owned by that actor/model/text.");
    talk.Start(actor, steps, 0); talk.Tick(90000);
    Check(native.Opens[^1].Text == "second", "Late frames must not skip and burst several NPC lines.");
    var count = native.Opens.Count; talk.Tick(90000);
    Check(native.Opens.Count == count, "Repeated ticks at the same time cannot advance another line.");
    speechEligible = false; talk.Tick(100000);
    Check(!talk.Running && native.Opens.Count == count, "Lost local ownership nonce must cancel speech without native access.");
    var closed = native.Closes.Count; talk.Clear(actor);
    Check(native.Closes.Count == closed, "Invalid owned actors must not receive stale close calls.");
    speechEligible = true;
    talk.Start(actor, steps, 0); talk.ForgetScene(); talk.Tick(100000);
    Check(!talk.Running && native.Closes.Count == closed, "Scene forget must not dereference last territory's actors.");
    talk.Start(actor, steps, 0); s.Restore(actor); talk.Tick(7000);
    Check(!talk.Running, "Restoring or redrawing a model cancels its pending speech.");
    s.Tick(); s.Apply(actor, "original", "speaker", State(18, 2)); s.Tick();
    talk.Start(actor, steps, 0); s.Apply(actor, "original", "another speaker", State(18, 2)); s.Tick();
    count = native.Opens.Count; talk.Tick(7000);
    Check(!talk.Running && native.Opens.Count == count, "Reusing the same model for another named hunt must not replay the previous hunt's dialogue.");
    s.Apply(actor, "original", "speaker", State(18, 2)); s.Tick();
    talk.Start(actor, steps, 0); talk.Reset();
    Check(!talk.Running && native.Closes[^1].Item3 == "first\nline", "Reset closes the bubble and cancels the remaining sequence.");
    foreach (var invalid in new[] { "", "   ", "bad\0text", "bad\u0002payload", new string('中', 684) })
    {
        var rejected = false;
        try { talk.Play(actor, invalid, 5); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid and oversized plain text must never reach the native string API.");
    }
    foreach (var seconds in new[] { 0f, -1f, 16f, float.NaN, float.PositiveInfinity })
    {
        var rejected = false;
        try { talk.Play(actor, "valid", seconds); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Native bubbles must always receive a bounded finite lifetime.");
    }
    Check(HuntDialogueCatalog.Normalize("  首句\r\n次句\r末句  ") == "首句\n次句\n末句", "Line ending normalization must preserve multiline NPC dialogue.");
}

if (args.Length == 1)
{
    using var game = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.ChineseSimplified });
    var catalog = HuntCatalog.Load(game.Excel.GetSheet<TerritoryType>(Language.ChineseSimplified),
        game.Excel.GetSheet<BNpcName>(Language.ChineseSimplified), game.Excel.GetSheet<PlaceName>(Language.ChineseSimplified));
    Console.WriteLine("Catalog: " + string.Join(", ", catalog.GroupBy(h => h.RankName).Select(g => $"{g.Key}={g.Count()}")));
    Check(catalog.Count == 207 && catalog.Count(h => !h.IsSpecial) == 201,
        "The installed client must supply 201 regular hunts and six requested special appearances.");
    Check(catalog.Count(h => h.FilterRank == 3) == 47 && catalog.Count(h => h.FilterRank == 4) == 3
        && catalog.Count(h => h.FilterRank == 5) == 3, "S, SS bosses and SS adds must be separate filters.");
    Check(catalog.Where(h => h.IsSpecial).Select(h => h.NameId).Order().SequenceEqual(new uint[] {8915,8916,10615,10616,13406,13407}),
        "Only the six explicitly requested special actors may enter the special catalog.");
    foreach (var special in catalog.Where(h => h.IsSpecial))
    {
        Check(special.IsAggressive && special.RankName is "SS" or "SS随从" && special.TerritoryIds?.Length == 6,
            $"Special actors must be aggressive, correctly labelled and deduplicated across six maps: {special.Name}");
        Check(special.MatchesTerritory(0) && special.TerritoryIds!.All(special.MatchesTerritory)
            && !special.MatchesTerritory(uint.MaxValue), "Special map filters must cover their actual expansion territories only.");
        var expectedMaps = catalog.Where(h => !h.IsSpecial && h.Expansion == special.Expansion).Select(h => h.TerritoryId).Distinct().Order();
        Check(special.TerritoryIds!.Order().SequenceEqual(expectedMaps), "Every special must retain all six map associations instead of only its first occurrence.");
    }
    var dialogue = new HuntDialogueCatalog(catalog);
    var npcYells = game.Excel.GetSheet<NpcYell>(Language.ChineseSimplified)!;
    Check(HuntDialogueCatalog.Profiles.Count == 47 && HuntDialogueCatalog.Profiles.Select(p => p.NameId).Distinct().Count() == 47,
        "Every S hunt must have its own audited dialogue status.");
    Check(HuntDialogueCatalog.Profiles.Sum(p => p.Rows.Length) == 61 && HuntDialogueCatalog.Profiles.Count(p => p.Rows.Length > 0) == 5,
        "Reviewed ownership must contain exactly the confirmed 61 rows for five hunts.");
    foreach (var p in HuntDialogueCatalog.Profiles)
    {
        Check(catalog.Any(h => h.Rank == 3 && h.NameId == p.NameId && h.Name == p.Name && h.ModelId == p.ModelId)
            && p.WikiUrl.StartsWith("https://ff14.huijiwiki.com/wiki/"), "Each dialogue source must belong to the same current CN S hunt.");
        foreach (var mapped in p.Rows)
        {
            Check(HuntDialogueCatalog.Matches(mapped, npcYells.GetRow(mapped.RowId).Text.ExtractText()),
                $"Confirmed NpcYell ID/text changed: {p.Name}/{mapped.RowId}");
            Check(!HuntDialogueCatalog.Matches(mapped, "unrelated text") && !HuntDialogueCatalog.Matches(mapped, ""),
                "Changed or missing row text must disable a mapping rather than misattribute dialogue.");
        }
    }
    dialogue.LoadBuiltIns(npcYells); dialogue.LoadBuiltIns(npcYells);
    Check(dialogue.All.Length == 44 && dialogue.All.Select(l => l.NameId).Distinct().Count() == 5,
        "Repeated loading must deduplicate 61 directional row variants into 44 distinct spoken lines.");
    foreach (var h in catalog.Where(h => h.Rank == 3))
    {
        Check(dialogue.Speaker(h.Name) == h.NameId && dialogue.Capture(h.NameId, $"收集测试：{h.Name}"),
            "Real NPC speech capture must accept every regular S and requested SS hunt.");
        Check(!dialogue.Capture(h.NameId, $"收集测试：{h.Name}"), "Repeated NPC chat must not duplicate saved dialogue.");
    }
    Check(dialogue.Speaker("玩家随便说的话") is null && dialogue.Speaker("前缀伊休妲") is null
        && !dialogue.Capture(0, "unknown") && !dialogue.Capture(catalog.First(h => h.Rank == 2).NameId, "A hunt"),
        "Player names, partial matches, unknown IDs and A hunts must not enter the S-hunt library.");
    for (var i = 0; i < 140; i++) dialogue.Capture(10617, $"bounded-{i}");
    Check(dialogue.All.Count(l => l.NameId == 10617) == HuntDialogueCatalog.MaxPerHunt, "Per-hunt captured speech must be bounded.");
    var savedDialogue = System.Text.Json.JsonSerializer.Deserialize<HuntDialogue[]>(System.Text.Json.JsonSerializer.Serialize(dialogue.All))!;
    Check(savedDialogue.SequenceEqual(dialogue.All), "Saved text, hunt ownership, line breaks and timing must survive a JSON round trip.");
    Check(catalog.Count(h => h.FilterRank == 1) == 77 && catalog.Count(h => h.FilterRank == 2) == 77, "B/A filters must exclude SS minions.");
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
    Check(catalog.Where(h => !h.IsAggressive).Count() == 77 && catalog.Where(h => h.IsAggressive).Count() == 130,
        "Regular B previews are passive; regular A/S and all requested special actors are aggressive.");
    Check(catalog.Single(h => h.Name == "第十三个孩子") is { Rank: 1, IsAggressive: false, Battalion: 4, NameplateIcon: 1 },
        "The Thirteenth Child must retain its blue passive elite icon without becoming a friendly NPC.");
    foreach (var h in catalog)
    {
        var iconState = State(h.ModelId, h.Scale) with { NameplateIcon = h.NameplateIcon };
        var iconEntry = new SwapEntry(actor, "source", h.Name, original, iconState, false, false);
        Check(NameplateIconPolicy.Resolve(iconEntry, catalog) == (h.IsAggressive ? 61710 : 61704),
            $"Every replacement must select its target hunt's native nameplate icon: {h.Name}");
    }
    Check(game.Excel.GetSheet<BNpcName>().GetRow(541).Singular.ToString() == "木人"
        && game.Excel.GetSheet<BNpcName>().GetRow(13078).Singular.ToString() == "老朽的木人",
        "Dummy identity rows must match the installed CN client; do not classify by a swapped display name.");
    Check(StrikingDummyPolicy.UsesLayoutModel(541, game.Excel.GetSheet<BNpcBase>().GetRow(901).ModelChara.Value.Type)
        && !StrikingDummyPolicy.UsesLayoutModel(541, game.Excel.GetSheet<BNpcBase>().GetRow(16845).ModelChara.Value.Type)
        && !StrikingDummyPolicy.UsesLayoutModel(541, game.Excel.GetSheet<BNpcBase>().GetRow(11744).ModelChara.Value.Type)
        && !StrikingDummyPolicy.UsesLayoutModel(33, 0),
        "Only confirmed layout-based dummies use a linked visual; trial/explorer dummies and unrelated layout NPCs keep their own paths.");
    unsafe
    {
        NativeCharacter dummy = default;
        FFXIVClientStructs.FFXIV.Client.Graphics.Scene.DrawObject dummyDraw = default;
        dummy.GameObject.DrawObject = &dummyDraw;
        dummyDraw.Object.Scale = new(1.25f);
        var dummyBase = game.Excel.GetSheet<BNpcBase>().GetRow(901);
        dummy.ModelContainer.ModelCharaId = (int)dummyBase.ModelChara.RowId;
        dummy.ModelContainer.ModelCharaId_2 = -1;
        dummy.ModelScale = dummy.GameObject.Scale = dummyBase.Scale;
        dummy.GameObject.BaseId = 901;
        dummy.NameId = 541;
        dummy.Health = 1234; dummy.MaxHealth = 5678;
        dummy.Level = 100; dummy.Icon = dummyBase.Rank;
        dummy.Battalion = (byte)dummyBase.Battalion.RowId;
        dummy.Flags = 0xa2;
        dummy.GameObject.HitboxRadius = 0.75f;
        DisplayName.Encode("木人").CopyTo(dummy.GameObject.Name);
        var before = NativeAppearanceCodec.Capture(&dummy);
        foreach (var hunt in catalog)
        {
            var replacement = HuntAppearanceReplacement.Create(before, hunt, hunt.Scale);
            NativeAppearanceCodec.Write(&dummy, replacement);
            var entry = new SwapEntry(actor with { NameId = 541, DataId = 901 }, "木人", hunt.Name, before, replacement, false, false);
            Check(dummy.ModelContainer.ModelCharaId == hunt.ModelId && dummy.GameObject.NameString == hunt.Name
                && dummy.Level == hunt.Level && dummy.Icon == hunt.NameplateIcon
                && dummy.ModelScale == hunt.Scale && dummy.GameObject.Scale == hunt.Scale
                && (System.Numerics.Vector3)dummyDraw.Object.Scale == new System.Numerics.Vector3(hunt.Scale)
                && NameplateLevelPolicy.Resolve(entry) == $"{hunt.Level}级 "
                && NameplateIconPolicy.Resolve(entry, catalog) == (hunt.IsAggressive ? 61710 : 61704),
                $"A dummy must use every hunt's own model, name, numeric/rendered level, elite icon and native scale: {hunt.Name}");
            Check(dummy.NameId == 541 && dummy.GameObject.BaseId == 901 && dummy.Health == 1234 && dummy.MaxHealth == 5678
                && dummy.Flags == 0xa2 && dummy.Battalion == 4 && dummy.GameObject.HitboxRadius == 0.75f,
                $"Replacing a dummy must preserve battle identity, health, aggression, battalion and hitbox: {hunt.Name}");
            NativeAppearanceCodec.Write(&dummy, before);
            Check(dummy.GameObject.NameString == "木人" && dummy.Level == 100 && dummy.Icon == before.NameplateIcon
                && dummy.ModelContainer.ModelCharaId == before.ModelId && dummy.ModelScale == before.ModelScale
                && (System.Numerics.Vector3)dummyDraw.Object.Scale == before.DrawScale,
                $"Restoring after {hunt.Name} must restore the actual dummy's original presentation.");
        }
    }
    foreach (var id in new[] { NameplateIconPolicy.PassiveElite, NameplateIconPolicy.AggressiveElite })
        Check(game.FileExists($"ui/icon/061000/{id:D6}.tex"), "Selected native nameplate icon resources must exist in the installed client.");
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
    Check(timelines.GetRow(NativeCombatPresentationCodec.NormalIdle).Key.ToString() == "normal/idle",
        "The normal stance timeline must match the installed CN client's generic animation key.");
    Check(timelines.GetRow(NativeCombatPresentationCodec.BattleIdle).Key.ToString() == "battle/idle",
        "The battle stance timeline must match the installed CN client's generic animation key.");
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
    Check(HuntSkillPresets.All.Count == 207 && HuntSkillPresets.All.Sum(p => p.Skills.Length) == 802,
        "Every regular S/A/B and requested special actor must have its own reviewed skill profile.");
    Check(HuntSkillPresets.All.Where(p => catalog.Any(h => h.NameId == p.NameId && h.FilterRank == 3)).Sum(p => p.Skills.Length) == 300,
        "Extending A/B and special profiles must preserve all 300 original S-hunt skill entries.");
    Check(HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "record")) == 346
        && HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "resource")) == 340
        && HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "variants")) == 100
        && HuntSkillPresets.All.Sum(p => p.Skills.Count(s => s.Mode == "unavailable")) == 16,
        "Known action records, unique resource mappings, ambiguous variants and unavailable releases must stay distinct.");
    Check(HuntSkillPresets.All.Where(p => catalog.Any(h => h.NameId == p.NameId && h.FilterRank == 3))
        .All(p => p.Skills.Any(s => s.Primary is not null)), "All original regular S profiles must retain a direct playback mapping.");
    Check(HuntSkillPresets.Profile(8916)!.Skills.Single(s => s.Name == "石化之眼").Primary is { ActionId: 18041, ActionName: "石化眼" },
        "The requested Forgiven Gossip must use its recorded action and validate the current CN spelling.");
    foreach (var skillProfile in HuntSkillPresets.All)
    {
        var hunt = catalog.Single(h => h.NameId == skillProfile.NameId);
        var family = SkillCatalog.ModelFamily(game.Excel.GetSheet<ModelChara>().GetRow((uint)hunt.ModelId));
        Check(hunt.Name == skillProfile.Name && hunt.ModelId == skillProfile.ModelId
            && skillProfile.Skills.Select(s => s.Name).Distinct().Count() == skillProfile.Skills.Length
            && skillProfile.WikiUrl.StartsWith("https://ff14.huijiwiki.com/wiki/", StringComparison.Ordinal),
            $"Skill skillProfiles must identify the correct S hunt and retain its Wiki source: {skillProfile.Name}");
        Check(skillProfile.Skills.Any(s => HuntSkillPresets.Resolve(s.Name, s.Primary, skills) is not null
            || s.Variants.Any(v => HuntSkillPresets.Resolve(s.Name, v, skills) is not null)),
            $"Every skill library must include at least one playable or explicit trial animation mapping: {skillProfile.Name}");
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
                    || mapping.TimelineKey.StartsWith("magic/", StringComparison.Ordinal)
                    || item.Mode == "record" && skillProfile.RecordSource is not null
                        && mapping.TimelineKey.StartsWith("mon_sp/gimmick/", StringComparison.Ordinal)
                    || hunt.NameId == 4361 && mapping.TimelineKey.StartsWith("ws/bt_swd_sld/", StringComparison.Ordinal)
                    || hunt.NameId == 2926 && mapping.TimelineKey.StartsWith("ws/bt_2sp_emp/", StringComparison.Ordinal))
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
    public bool RepairDummies { get; set; }
    public bool NeedsRedraw(ActorIdentity actor, VisualState state)
        => RepairDummies && StrikingDummyPolicy.NeedsRedraw(actor.NameId, Actors[actor].ModelId, Actors[actor].SecondaryModelId, false, state);
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

sealed class FakeDialogueAccess : IDialogueAccess
{
    public List<(ActorIdentity Actor, int Model, string Text, float Seconds)> Opens { get; } = [];
    public List<(ActorIdentity, int, string)> Closes { get; } = [];
    public void OpenBubble(ActorIdentity actor, int modelId, string text, float seconds) => Opens.Add((actor, modelId, text, seconds));
    public void CloseBubble(ActorIdentity actor, int modelId, string expectedText) => Closes.Add((actor, modelId, expectedText));
}

sealed class FakeAnimationAccess : IAnimationAccess, IDirectionalAnimationAccess
{
    public bool Active { get; set; }
    public List<(ActorIdentity Actor, int Model, ushort Timeline)> Stopped { get; } = [];
    public bool IsAnimationActive(ActorIdentity actor, int expectedModelId, ushort timelineId) => Active && !Unavailable;
    public void StopAnimation(ActorIdentity actor, int expectedModelId, ushort timelineId)
    {
        Stopped.Add((actor, expectedModelId, timelineId)); Active = false;
    }
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
    private readonly Dictionary<(ushort, nint, ActorIdentity, ulong), byte> leases = [];
    private static (ushort, nint, ActorIdentity, ulong) LeaseKey(LocalActorLease lease)
        => (lease.ClientIndex, lease.Manager, lease.Actor, lease.Token);
    public List<LocalActorLease> Deleted { get; } = [];
    public List<LocalActorLease> Refreshed { get; } = [];
    public bool FailDelete { get; set; }
    public bool FailDraw { get; set; }
    public List<LocalPlacement> Placements { get; } = [];
    public LocalSpawn CreateAt(HuntAppearance hunt, float scale, LocalPlacement placement)
    {
        placement.Validate(); Placements.Add(placement);
        return Create(hunt, scale, 8);
    }
    public LocalSpawn Create(HuntAppearance hunt, float scale, float distance)
    {
        var index = next++;
        var actor = new ActorIdentity(index, (2UL << 32) | index, 0, 0, 0x10000 + index);
        var lease = new LocalActorLease((ushort)(index - 200), 0x5000, actor, index, hunt.Battalion, hunt.IsAggressive);
        var state = ModelSize.Apply(new VisualState(hunt.ModelId, -1, hunt.Scale, hunt.Customize, hunt.Equipment,
            [0, 0, 0], 0, 0, LocalPreviewName.Stamp(DisplayName.Encode(hunt.Name), lease.Token), hunt.Level,
            hunt.Scale, new System.Numerics.Vector3(hunt.Scale)), scale);
        access.Actors.Add(actor, state);
        if (FailDraw) access.FailBeginFor = actor;
        leases.Add(LeaseKey(lease), 0);
        return new(lease, state);
    }
    public bool Exists(LocalActorLease lease) => leases.ContainsKey(LeaseKey(lease)) && access.Actors.TryGetValue(lease.Actor, out var state)
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
        leases.Remove(LeaseKey(lease));
        access.Actors.Remove(lease.Actor);
        access.Hidden.Remove(lease.Actor);
    }
}

sealed class FakeLinkedAccess(FakeAccess actors) : ILinkedDummyAccess
{
    public CombatPresentation Combat { get; set; }
    public List<(LocalActorLease Visual, CombatPresentation Combat, bool ChangePose)> CombatWrites { get; } = [];
    public CombatPresentation ReadCombat(ActorIdentity source) => Combat;
    public void SyncCombat(LocalActorLease visual, CombatPresentation combat, bool changePose)
        => CombatWrites.Add((visual, combat, changePose));
    public HashSet<ActorIdentity> Hidden { get; } = [];
    public List<(ActorIdentity, ActorIdentity)> Aligned { get; } = [];
    public List<(ActorIdentity, bool)> Restores { get; } = [];
    public bool Ready { get; set; } = true;
    public bool Forgotten { get; private set; }
    public bool Exists(ActorIdentity source) => actors.Exists(source);
    public LocalPlacement Placement(ActorIdentity source) => new(new(10, 20, 30), 1.25f, false);
    public bool IsHidden(ActorIdentity source) => Hidden.Contains(source);
    public void Hide(ActorIdentity source) => Hidden.Add(source);
    public void RestoreVisibility(ActorIdentity source, bool hidden)
    {
        Restores.Add((source, hidden));
        if (hidden) Hidden.Add(source); else Hidden.Remove(source);
    }
    public void Align(LocalActorLease visual, ActorIdentity source) => Aligned.Add((visual.Actor, source));
    public bool IsReady(LocalActorLease visual) => Ready;
    public void ForgetScene() => Forgotten = true;
}

sealed class FakePauseAccess(FakeAccess actors) : IAnimationPauseAccess
{
    public Dictionary<ActorIdentity, AnimationClockState> Clocks { get; } = [];
    public int Writes { get; private set; }
    public bool FailFreeze { get; set; }
    public ActorIdentity? FailRestore { get; set; }
    public bool IsCurrent(ActorIdentity actor, int modelId) => actors.Actors.TryGetValue(actor, out var state) && state.ModelId == modelId;
    public AnimationClockState Capture(ActorIdentity actor, int modelId) => Clocks[actor];
    public void Write(ActorIdentity actor, int modelId, AnimationClockState state)
    {
        if (FailRestore == actor) throw new InvalidOperationException("Simulated restore failure.");
        Clocks[actor] = state with { SlotSpeeds = state.SlotSpeeds.ToArray() }; Writes++;
    }
    public void Freeze(ActorIdentity actor, int modelId)
    {
        Clocks[actor] = new(0, new float[14]); Writes++;
        if (FailFreeze) throw new InvalidOperationException("Simulated partial freeze failure.");
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

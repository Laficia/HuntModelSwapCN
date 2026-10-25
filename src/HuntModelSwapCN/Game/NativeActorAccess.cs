using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using HuntModelSwapCN.Catalog;
using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeActorAccess(IObjectTable objects, INamePlateGui nameplates) : IActorAccess, IAnimationAccess, IDirectionalAnimationAccess, IDialogueAccess
{
    public Func<ActorIdentity, bool>? KeepHidden { private get; set; }
    public static ActorIdentity Identity(IBattleNpc npc)
        => new(npc.ObjectIndex, npc.GameObjectId, npc.BaseId, npc.NameId, npc.Address);

    public IBattleNpc? Resolve(ActorIdentity actor)
    {
        if (actor.Index >= objects.Length || objects[actor.Index] is not IBattleNpc npc
            || !npc.IsValid() || npc.Address == 0 || Identity(npc) != actor) return null;
        return npc;
    }

    public bool Exists(ActorIdentity actor) => Resolve(actor) is not null;
    public bool HasRealHunt(uint nameId) => objects.OfType<IBattleNpc>()
        .Any(n => n.IsValid() && n.Address != 0 && n.NameId == nameId);
    public float? Heading(ActorIdentity actor, int expectedModelId)
    {
        if (Resolve(actor) is not { } npc) return null;
        var c = (NativeCharacter*)npc.Address;
        return c->ModelContainer.ModelCharaId == expectedModelId && float.IsFinite(c->GameObject.Rotation)
            ? ModelRotation.Degrees(c->GameObject.Rotation) : null;
    }

    private NativeCharacter* AnimationCharacter(ActorIdentity actor, int expectedModelId)
    {
        if (CanApply(actor) is { } reason) throw new InvalidOperationException(reason);
        var c = Character(actor);
        if (c->ModelContainer.ModelCharaId != expectedModelId)
            throw new InvalidOperationException("目标模型已变化，请重新替换后再播放。");
        if (c->GameObject.DrawObject == null || c->GameObject.DrawObject->LoadState != 3)
            throw new InvalidOperationException("目标模型仍在加载，请稍后再播放。");
        return c;
    }

    public float Rotation(ActorIdentity actor, int expectedModelId) => AnimationCharacter(actor, expectedModelId)->GameObject.Rotation;

    public void Face(ActorIdentity actor, int expectedModelId, float rotation)
    {
        if (!float.IsFinite(rotation)) throw new InvalidOperationException("演示朝向无效。");
        var c = AnimationCharacter(actor, expectedModelId);
        if (MathF.Abs(MathF.IEEERemainder(c->GameObject.Rotation - rotation, MathF.Tau)) > 0.001f)
            c->GameObject.SetRotation(rotation);
    }

    public void PlayAnimation(ActorIdentity actor, int expectedModelId, ushort timelineId)
    {
        var c = AnimationCharacter(actor, expectedModelId);
        // Playback only: never call ActionManager.UseAction or synthesize action-effect packets.
        c->Timeline.TimelineSequencer.PlayTimeline(timelineId);
    }

    public void OpenBubble(ActorIdentity actor, int modelId, string text, float seconds)
    {
        var c = AnimationCharacter(actor, modelId);
        if (FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloon.Addresses.OpenBalloon.Value == 0
            || FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloon.Addresses.CloseBalloon.Value == 0)
            throw new InvalidOperationException("当前客户端未解析 NPC 气泡接口。");
        if (c->YellBalloon.Character != c) throw new InvalidOperationException("对象的原生气泡尚未初始化。");
        // Native attachment follows the rendered skeleton and its scale. No synthetic chat log entry.
        c->YellBalloon.OpenBalloon(text, seconds, false, 0, false, false, false, byte.MaxValue);
    }

    public void CloseBubble(ActorIdentity actor, int modelId, string expectedText)
    {
        if (Resolve(actor) is not { } npc) return;
        var c = (NativeCharacter*)npc.Address;
        if (c->ModelContainer.ModelCharaId != modelId || c->YellBalloon.Character != c
            || c->YellBalloon.State == FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloonState.Inactive
            || c->YellBalloon.Text.ToString() != expectedText) return;
        // A subsequent server-owned balloon with different text must be left alone.
        if (FFXIVClientStructs.FFXIV.Client.Game.NpcYellBalloon.Addresses.CloseBalloon.Value != 0)
            c->YellBalloon.CloseBalloon();
    }

    public string? CanApply(ActorIdentity actor)
    {
        if (Resolve(actor) is not { } npc) return "请选中一只已加载的战斗小怪或可攻击木人。";
        var character = (NativeCharacter*)npc.Address;
        return ReplacementTargetPolicy.CannotApply(new(npc.ObjectKind == ObjectKind.BattleNpc,
            (byte)npc.BattleNpcKind, npc.NameId, npc.OwnerId, npc.CurrentHp,
            character->TransformationId != 0, character->GameObject.DrawObject != null));
    }

    public string Describe(IGameObject? target)
    {
        if (target is null || !target.IsValid() || target.Address == 0) return "未选中有效目标。";
        var text = $"目标={target.Name}，类型={target.ObjectKind}，子类型={target.SubKind}，BaseId={target.BaseId}";
        if (target is not IBattleNpc npc || !npc.IsValid() || npc.Address == 0) return text;
        var c = (NativeCharacter*)npc.Address;
        return text + $"，NameId={npc.NameId}，OwnerId={npc.OwnerId:X8}，HP={npc.CurrentHp}/{npc.MaxHp}"
            + $"，Model={c->ModelContainer.ModelCharaId}，Draw={(c->GameObject.DrawObject == null ? "无" : c->GameObject.DrawObject->LoadState.ToString())}"
            + $"，Layout={(c->GameObject.SharedGroupLayoutInstance == null ? "无" : "有")}，Render={c->GameObject.RenderFlags}，Targetable={c->GameObject.TargetableStatus}"
            + $"，Transform={c->TransformationId}，Scale={c->ModelScale:G}/{c->GameObject.Scale:G}";
    }

    private NativeCharacter* Character(ActorIdentity actor)
        => Resolve(actor) is { } npc ? (NativeCharacter*)npc.Address
            : throw new InvalidOperationException("目标已变化或离开加载范围。");

    public VisualState Read(ActorIdentity actor)
    {
        var state = NativeAppearanceCodec.Capture(Character(actor));
        NativeAppearanceCodec.Validate(state);
        return state;
    }

    public static VisualState Replacement(VisualState current, HuntAppearance appearance, float scale)
        => HuntAppearanceReplacement.Create(current, appearance, scale);

    public bool NeedsRedraw(ActorIdentity actor, VisualState applied)
    {
        if (!StrikingDummyPolicy.IsDummy(actor.NameId) || CanApply(actor) is not null) return false;
        var c = Character(actor);
        return StrikingDummyPolicy.NeedsRedraw(actor.NameId, c->ModelContainer.ModelCharaId,
            c->ModelContainer.ModelCharaId_2, c->TransformationId != 0, applied);
    }

    public void BeginRedraw(ActorIdentity actor, VisualState state)
    {
        NativeAppearanceCodec.Validate(state);
        var c = Character(actor);
        c->GameObject.DisableDraw();
        NativeAppearanceCodec.Write(c, state);
        nameplates.RequestRedraw();
    }

    // Runs on the following framework update, allowing a game tick between hiding and showing.
    public void FinishRedraw(ActorIdentity actor)
    {
        if (KeepHidden?.Invoke(actor) == true) return;
        Character(actor)->GameObject.EnableDraw();
        nameplates.RequestRedraw();
    }

    public void RefreshPresentation(ActorIdentity actor, VisualState state)
    {
        var c = Character(actor);
        NativeAppearanceCodec.WriteScale(c, state);
        if (NativeAppearanceCodec.WritePresentation(c, state)) nameplates.RequestRedraw();
    }
}

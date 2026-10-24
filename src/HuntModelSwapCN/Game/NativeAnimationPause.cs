using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeAnimationPause : IAnimationPauseAccess, IDisposable
{
    private delegate bool OverallSpeedDelegate(TimelineContainer* timeline);
    private delegate void SlotSpeedDelegate(ActionTimelineSequencer* sequencer, uint slot, float speed);
    // Strong references retained for the full hook lifetime, including disposal.
    private readonly OverallSpeedDelegate overallDetour;
    private readonly SlotSpeedDelegate slotDetour;
    private readonly IGameInteropProvider interop;
    private readonly IPluginLog log;
    private readonly NativeActorAccess actors;
    private Hook<OverallSpeedDelegate>? overallHook;
    private Hook<SlotSpeedDelegate>? slotHook;
    private bool errorReported;
    public Func<nint, bool>? ShouldPause { private get; set; }
    public Func<ActorIdentity, bool>? CanControl { private get; set; }
    private readonly Dictionary<nint, (ActorIdentity Actor, int ModelId)> pendingRefresh = [];

    public NativeAnimationPause(NativeActorAccess actors, IGameInteropProvider interop, IPluginLog log)
    {
        this.actors = actors;
        this.interop = interop;
        this.log = log;
        overallDetour = OnOverallSpeed;
        slotDetour = OnSlotSpeed;
    }

    private void EnsureHooks()
    {
        if (overallHook is not null && slotHook is not null) return;
        try
        {
            var overall = TimelineContainer.Addresses.CalculateAndApplyOverallSpeed.Value;
            var slots = ActionTimelineSequencer.Addresses.SetSlotSpeed.Value;
            if (overall == 0 || slots == 0) throw new InvalidOperationException("当前客户端动画接口不可用。");
            overallHook = interop.HookFromAddress<OverallSpeedDelegate>(overall, overallDetour);
            slotHook = interop.HookFromAddress<SlotSpeedDelegate>(slots, slotDetour);
            overallHook.Enable();
            slotHook.Enable();
        }
        catch
        {
            slotHook?.Dispose(); overallHook?.Dispose(); slotHook = null; overallHook = null;
            throw;
        }
    }

    private bool Paused(nint owner)
    {
        try { return owner != 0 && (ShouldPause?.Invoke(owner) ?? false); }
        catch (Exception ex)
        {
            if (!errorReported) { errorReported = true; log.Error(ex, "核验暂停动画对象失败，已放行原生速度更新"); }
            return false;
        }
    }

    private bool OnOverallSpeed(TimelineContainer* timeline)
    {
        var changed = overallHook!.Original(timeline);
        if (timeline != null && Paused((nint)timeline->OwnerObject))
        {
            timeline->OverallSpeed = 0;
            return true;
        }
        // Returning changed forces the caller to propagate restored speeds even
        // when the overall scalar already equals the game's calculated value.
        if (timeline != null && pendingRefresh.Remove((nint)timeline->OwnerObject, out var pending))
        {
            try
            {
                if (IsCurrent(pending.Actor, pending.ModelId) && (CanControl?.Invoke(pending.Actor) ?? false)) return true;
            }
            catch (Exception ex)
            {
                if (!errorReported) { errorReported = true; log.Error(ex, "恢复动画刷新对象已不可用"); }
            }
        }
        return changed;
    }

    private void OnSlotSpeed(ActionTimelineSequencer* sequencer, uint slot, float speed)
        => slotHook!.Original(sequencer, slot,
            sequencer != null && Paused((nint)sequencer->Parent) ? 0 : speed);

    public bool IsCurrent(ActorIdentity actor, int modelId) => actors.Resolve(actor) is { } npc
        && ((NativeCharacter*)npc.Address)->ModelContainer.ModelCharaId == modelId;
    private NativeCharacter* Character(ActorIdentity actor, int modelId)
        => IsCurrent(actor, modelId) ? (NativeCharacter*)actor.Address
            : throw new InvalidOperationException("演示对象已变化，未写入动画速度。");

    public AnimationClockState Capture(ActorIdentity actor, int modelId)
    {
        var c = Character(actor, modelId);
        if (c->GameObject.DrawObject == null || c->GameObject.DrawObject->LoadState != 3)
            throw new InvalidOperationException("请等待模型加载完成，再暂停动作。");
        EnsureHooks(); // Never install native hooks merely by loading the plugin or opening its window.
        return NativeAnimationClockCodec.Capture(c);
    }

    public void Write(ActorIdentity actor, int modelId, AnimationClockState state)
    {
        var c = Character(actor, modelId);
        NativeAnimationClockCodec.Write(c, state, (slot, speed) => c->Timeline.TimelineSequencer.SetSlotSpeed(slot, speed));
        pendingRefresh[actor.Address] = (actor, modelId);
    }

    public void Freeze(ActorIdentity actor, int modelId)
    {
        var c = Character(actor, modelId);
        c->Timeline.OverallSpeed = 0;
        for (uint slot = 0; slot < 14; slot++) c->Timeline.TimelineSequencer.SetSlotSpeed(slot, 0);
    }

    public void Dispose()
    {
        ShouldPause = null;
        CanControl = null;
        pendingRefresh.Clear();
        slotHook?.Dispose(); overallHook?.Dispose();
        GC.KeepAlive(overallDetour); GC.KeepAlive(slotDetour);
    }

    public void PrunePending()
    {
        foreach (var pending in pendingRefresh.Values.ToArray())
            if (!IsCurrent(pending.Actor, pending.ModelId) || !(CanControl?.Invoke(pending.Actor) ?? false))
                pendingRefresh.Remove(pending.Actor.Address);
    }
}

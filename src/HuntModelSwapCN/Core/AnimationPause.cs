namespace HuntModelSwapCN.Core;

public sealed record AnimationClockState(float OverallSpeed, float[] SlotSpeeds)
{
    public void Validate()
    {
        if (SlotSpeeds.Length != 14 || !float.IsFinite(OverallSpeed) || SlotSpeeds.Any(s => !float.IsFinite(s)))
            throw new InvalidOperationException("动画速度数据无效，未暂停。");
    }
}

public interface IAnimationPauseAccess
{
    bool IsCurrent(ActorIdentity actor, int modelId);
    AnimationClockState Capture(ActorIdentity actor, int modelId);
    void Write(ActorIdentity actor, int modelId, AnimationClockState state);
    void Freeze(ActorIdentity actor, int modelId);
}

public sealed class AnimationPause(SwapSession session, IAnimationPauseAccess access,
    Func<ActorIdentity, bool>? isEligible = null)
{
    private sealed record Entry(ActorIdentity Actor, int ModelId, AnimationClockState Original, bool FreezeFailed = false);
    private readonly Dictionary<ActorIdentity, Entry> entries = [];
    public IReadOnlyCollection<ActorIdentity> Actors => entries.Keys;
    private bool Valid(Entry entry) => session.Entries.Any(s => s.Actor == entry.Actor
        && s.Applied.ModelId == entry.ModelId && !s.WaitingForDraw && !s.Restoring)
        && (isEligible?.Invoke(entry.Actor) ?? true) && access.IsCurrent(entry.Actor, entry.ModelId);
    public bool IsPaused(ActorIdentity actor) => entries.TryGetValue(actor, out var entry) && Valid(entry);
    public bool IsPausedAddress(nint address) => entries.Values.Any(e => e.Actor.Address == address && Valid(e));

    public void Pause(ActorIdentity actor)
    {
        if (IsPaused(actor)) return;
        var applied = session.Entries.FirstOrDefault(e => e.Actor == actor)
            ?? throw new InvalidOperationException("请先生成预览或替换目标模型。");
        var entry = new Entry(actor, applied.Applied.ModelId, new(0, []));
        if (!Valid(entry)) throw new InvalidOperationException("请等待演示对象完成加载，或重新生成预览。");
        var state = access.Capture(actor, entry.ModelId);
        state.Validate();
        entry = entry with { Original = state with { SlotSpeeds = state.SlotSpeeds.ToArray() } };
        entries[actor] = entry; // Retain the snapshot even if a native write fails partway through.
        try { access.Freeze(actor, entry.ModelId); }
        catch { entries[actor] = entry with { FreezeFailed = true }; throw; }
    }

    public bool Resume(ActorIdentity actor)
    {
        if (!entries.Remove(actor, out var entry)) return false;
        if (!Valid(entry)) return false; // Never apply old speeds to a new model/owner.
        try { access.Write(actor, entry.ModelId, entry.Original); }
        catch { entries[actor] = entry; throw; }
        return true;
    }

    public void Tick()
    {
        foreach (var entry in entries.Values.ToArray())
        {
            if (!Valid(entry)) { entries.Remove(entry.Actor); continue; }
            if (entry.FreezeFailed) continue;
            try { access.Freeze(entry.Actor, entry.ModelId); }
            catch { entries[entry.Actor] = entry with { FreezeFailed = true }; throw; }
        }
    }

    public void Reset()
    {
        List<Exception> failures = [];
        foreach (var actor in entries.Keys.ToArray())
            try { Resume(actor); } catch (Exception ex) { failures.Add(ex); }
        if (failures.Count > 0) throw new AggregateException("部分动画速度恢复失败，可再次点击恢复。", failures);
    }

    public void ForgetScene() => entries.Clear();
}

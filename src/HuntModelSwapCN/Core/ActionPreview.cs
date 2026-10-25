using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public interface IAnimationAccess
{
    void PlayAnimation(ActorIdentity actor, int expectedModelId, ushort timelineId);
    bool IsAnimationActive(ActorIdentity actor, int expectedModelId, ushort timelineId) => false;
    void StopAnimation(ActorIdentity actor, int expectedModelId, ushort timelineId) { }
}

// All calls run on the framework thread. No repeating playback or server action requests.
public sealed class ActionPreview(SwapSession session, IAnimationAccess access)
{
    private sealed class Playback(int modelId, ushort timeline, long now)
    {
        public readonly int ModelId = modelId;
        public readonly ushort Timeline = timeline;
        public long LastTick = now;
        public long StartupRemaining = IntervalMs;
        public bool WasPaused;
    }
    private readonly Dictionary<ActorIdentity, Playback> playing = [];
    public Func<ActorIdentity, bool>? IsPaused { get; set; }
    public Func<ActorIdentity, bool>? IsEligible { get; set; }
    private long? lastPlayback;
    public const int IntervalMs = 750;

    public void Play(ActorIdentity actor, SkillAnimation skill, long now)
    {
        var entry = session.EntryFor(actor)
            ?? throw new InvalidOperationException("请先替换当前目标的模型，再播放技能动作。");
        if (entry.WaitingForDraw || entry.Restoring)
            throw new InvalidOperationException("目标正在重绘或恢复，请稍后再播放。");
        if (!(IsEligible?.Invoke(actor) ?? true)) throw new InvalidOperationException("演示对象已变化，请重新生成。");
        if (IsPaused?.Invoke(actor) ?? false) throw new InvalidOperationException("请先恢复动作，再播放技能。");
        if (skill.TimelineId == 0 || skill.Slot >= 14 || string.IsNullOrWhiteSpace(skill.TimelineKey))
            throw new InvalidOperationException("该技能没有可播放的动作时间轴。");
        if (lastPlayback.HasValue && now - lastPlayback.Value < IntervalMs)
            throw new InvalidOperationException("操作太快，请间隔至少 0.75 秒再播放。");
        access.PlayAnimation(actor, entry.Applied.ModelId, skill.TimelineId);
        playing[actor] = new(entry.Applied.ModelId, skill.TimelineId, now);
        lastPlayback = now;
    }

    public bool IsBusy(ActorIdentity actor, long now)
    {
        if (!playing.TryGetValue(actor, out var playback)) return false;
        var entry = session.EntryFor(actor);
        if (entry is null || entry.Restoring || entry.WaitingForDraw || entry.Applied.ModelId != playback.ModelId
            || !(IsEligible?.Invoke(actor) ?? true)) { playing.Remove(actor); return false; }
        var elapsed = Math.Max(0, now - playback.LastTick);
        playback.LastTick = now;
        if (IsPaused?.Invoke(actor) ?? false) { playback.WasPaused = true; return true; }
        playback.StartupRemaining = Math.Max(0, playback.StartupRemaining - (playback.WasPaused ? 0 : elapsed));
        playback.WasPaused = false;
        if (playback.StartupRemaining > 0 || access.IsAnimationActive(actor, playback.ModelId, playback.Timeline)) return true;
        playing.Remove(actor);
        return false;
    }

    public void Forget(ActorIdentity actor) => playing.Remove(actor);
    public void Stop(ActorIdentity actor)
    {
        if (!playing.TryGetValue(actor, out var playback)) return;
        var entry = session.EntryFor(actor);
        if (entry is not null && !entry.Restoring && !entry.WaitingForDraw && entry.Applied.ModelId == playback.ModelId
            && (IsEligible?.Invoke(actor) ?? true)) access.StopAnimation(actor, playback.ModelId, playback.Timeline);
        playing.Remove(actor);
    }
    public void Tick(long now) { foreach (var actor in playing.Keys.ToArray()) IsBusy(actor, now); }
    public void Clear() { lastPlayback = null; playing.Clear(); }
}

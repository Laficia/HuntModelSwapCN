using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public interface IAnimationAccess
{
    void PlayAnimation(ActorIdentity actor, int expectedModelId, ushort timelineId);
}

// All calls run on the framework thread. No repeating playback or server action requests.
public sealed class ActionPreview(SwapSession session, IAnimationAccess access)
{
    private long? lastPlayback;
    public const int IntervalMs = 750;

    public void Play(ActorIdentity actor, SkillAnimation skill, long now)
    {
        var entry = session.Entries.FirstOrDefault(e => e.Actor == actor)
            ?? throw new InvalidOperationException("请先替换当前目标的模型，再播放技能动作。");
        if (entry.WaitingForDraw || entry.Restoring)
            throw new InvalidOperationException("目标正在重绘或恢复，请稍后再播放。");
        if (skill.TimelineId == 0 || skill.Slot >= 14 || string.IsNullOrWhiteSpace(skill.TimelineKey))
            throw new InvalidOperationException("该技能没有可播放的动作时间轴。");
        if (lastPlayback.HasValue && now - lastPlayback.Value < IntervalMs)
            throw new InvalidOperationException("操作太快，请间隔至少 0.75 秒再播放。");
        access.PlayAnimation(actor, entry.Applied.ModelId, skill.TimelineId);
        lastPlayback = now;
    }

    public void Clear() => lastPlayback = null;
}

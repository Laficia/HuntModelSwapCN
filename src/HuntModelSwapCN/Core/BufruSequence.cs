using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public interface IDirectionalAnimationAccess
{
    float Rotation(ActorIdentity actor, int expectedModelId);
    void Face(ActorIdentity actor, int expectedModelId, float rotation);
}

// One framework-driven local animation sequence. No game actions, packets or combat effects.
public sealed class BufruSequence(SwapSession session, ActionPreview playback, IDirectionalAnimationAccess access,
    Func<ActorIdentity, bool>? isEligible = null)
{
    public const int ModelId = 3315;
    private static readonly string[] DirectionNames = ["前", "右", "后", "左"];
    private sealed class Run(ActorIdentity actor, float rotation, int[] directions, SkillAnimation[] skills, int holdMs)
    {
        public readonly ActorIdentity Actor = actor;
        public readonly float OriginalRotation = rotation;
        public readonly int[] Directions = directions;
        public readonly SkillAnimation[] Skills = skills;
        public readonly int HoldMs = holdMs;
        public int Step;
        public bool Holding;
        public long Deadline;
        public float Facing => MathF.IEEERemainder(OriginalRotation + Directions[Step % 5] * MathF.PI / 2, MathF.Tau);
        public SkillAnimation Skill => Skills[Step < 5 ? Step == 0 ? 0 : 1 : Step == 5 ? 2 : 3];
    }
    private Run? run;
    public bool Running => run is not null;
    public string Status { get; private set; } = "尚未开始连招。";
    public string DirectionOrder { get; private set; } = "";
    public float RemainingSeconds(long now) => run is { } r ? Math.Max(0, r.Deadline - now) / 1000f : 0;

    private bool Valid(ActorIdentity actor) => session.Entries.Any(e => e.Actor == actor
        && e.Applied.ModelId == ModelId && e.ReplacementName == "布弗鲁" && !e.WaitingForDraw && !e.Restoring)
        && (isEligible?.Invoke(actor) ?? true);

    public void Start(ActorIdentity actor, IReadOnlyDictionary<uint, SkillAnimation> skills,
        IReadOnlyList<int> directions, int holdMs, long now)
    {
        if (!Valid(actor)) throw new InvalidOperationException("请先生成布弗鲁预览或完成布弗鲁换模，并等待重绘结束。");
        if (directions.Count != 5 || directions.Any(d => d is < 0 or > 3) || holdMs is < 750 or > 10000)
            throw new InvalidOperationException("连招需要五次前/右/后/左方向，动作停留应为 0.75～10 秒。");
        var actions = new uint[] { 27307, 27308, 27309, 27310 }.Select(id => skills.TryGetValue(id, out var skill)
            && skill.CastTimeMs is > 0 and <= 60000 && skill.TimelineId > 0 && skill.Slot < 14
            && !string.IsNullOrWhiteSpace(skill.TimelineKey) ? skill
                : throw new InvalidOperationException($"布弗鲁连招技能 {id} 数据不可用。")).ToArray();
        Stop();
        var rotation = access.Rotation(actor, ModelId);
        if (!float.IsFinite(rotation)) throw new InvalidOperationException("演示对象朝向无效。");
        run = new(actor, rotation, directions.ToArray(), actions, holdMs);
        DirectionOrder = string.Join(" → ", run.Directions.Select(d => DirectionNames[d]));
        try { BeginCast(run, now); }
        catch { Stop("连招启动失败。"); throw; }
    }

    private void BeginCast(Run r, long now)
    {
        access.Face(r.Actor, ModelId, r.Facing);
        r.Holding = false;
        r.Deadline = now + r.Skill.CastTimeMs;
        Status = $"第 {r.Step % 5 + 1}/5 次{(r.Step < 5 ? "吸气" : "喷嚏")}：咏唱中，方向 {DirectionNames[r.Directions[r.Step % 5]]}";
    }

    public void Tick(long now)
    {
        if (run is not { } r) return;
        try
        {
            if (!Valid(r.Actor)) { Stop("连招已取消：演示对象已变化、恢复或正在重绘。"); return; }
            access.Face(r.Actor, ModelId, r.Facing);
            if (now < r.Deadline) return;
            if (!r.Holding)
            {
                playback.Play(r.Actor, r.Skill, now);
                r.Holding = true;
                r.Deadline = now + r.HoldMs;
                Status = $"第 {r.Step % 5 + 1}/5 次{(r.Step < 5 ? "吸气" : "喷嚏")}：动作播放，方向 {DirectionNames[r.Directions[r.Step % 5]]}";
            }
            else if (++r.Step == 10) Stop("已完成五次吸气和同顺序的五次喷嚏。");
            else BeginCast(r, now);
            // Never catch up multiple actions in one stalled frame; preserve every visible step.
        }
        catch { Stop("连招中断：对象或动作当前不可用。"); throw; }
    }

    public void Stop(string status = "已停止连招，剩余动作已取消。")
    {
        var previous = run;
        run = null;
        Status = status;
        if (previous is null) return;
        try { if (Valid(previous.Actor)) access.Face(previous.Actor, ModelId, previous.OriginalRotation); }
        catch { Status += " 对象已不可用，未恢复朝向。"; }
    }

    // Scene changes discard the old identity without writing its old slot in the new scene.
    public void ForgetScene() { run = null; DirectionOrder = ""; Status = "场景已变化，连招已取消。"; }
}

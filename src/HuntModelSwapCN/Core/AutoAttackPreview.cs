using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public enum AutoAttackVariant { Standard, MonsterA, MonsterB, Ranged }

public interface IAutoAttackAccess
{
    bool Ready(LocalActorLease lease, int expectedModelId);
    bool Prepare(LocalActorLease lease, int expectedModelId, bool facePlayer);
    void Play(LocalActorLease lease, int expectedModelId, ushort timeline);
    void Stop(LocalActorLease lease, int expectedModelId, ushort timeline);
}

// A clock for the single owned preview, never a server attack or an NPC AI loop.
public sealed class AutoAttackPreview(SwapSession session, IAutoAttackAccess access,
    Func<ActorIdentity, bool> paused, Func<ActorIdentity, long, bool> busy,
    Func<ActorIdentity, bool> rotationLocked)
{
    public const int DefaultIntervalMs = 2500;
    public bool Enabled { get; private set; } = true;
    public int IntervalMs { get; private set; } = DefaultIntervalMs;
    public AutoAttackVariant Variant { get; private set; }
    private LocalActorLease? lease;
    private int model;
    private ushort? active;
    private long? lastTick;
    private long remaining;
    private bool wasPaused;

    public static SkillAnimation Animation(AutoAttackVariant variant) => variant switch {
        AutoAttackVariant.Standard => new(0, "普通攻击", 112, "battle/auto_attack1", 1),
        AutoAttackVariant.MonsterA => new(0, "普通攻击（怪物 A）", 121, "battle/auto_attack1_mon_a", 1),
        AutoAttackVariant.MonsterB => new(0, "普通攻击（怪物 B）", 124, "battle/auto_attack1_mon_b", 1),
        AutoAttackVariant.Ranged => new(0, "普通攻击（怪物远程）", 127, "battle/auto_attack_shot1_mon", 1),
        _ => throw new InvalidOperationException("普通攻击动作版本无效。"),
    };

    public void Configure(bool enabled, int intervalMs, AutoAttackVariant variant)
    {
        if (intervalMs is < 1000 or > 10000 || !Enum.IsDefined(variant))
            throw new InvalidOperationException("平A间隔应为 1～10 秒，请选择有效动作版本。");
        if (Enabled == enabled && IntervalMs == intervalMs && Variant == variant) return;
        Interrupt();
        Enabled = enabled; IntervalMs = intervalMs; Variant = variant;
        remaining = 0;
    }

    private bool Valid(LocalActorLease candidate, out int expectedModel)
    {
        var entry = session.EntryFor(candidate.Actor);
        expectedModel = entry?.Applied.ModelId ?? 0;
        return entry is { WaitingForDraw: false, Restoring: false, PresentationOnly: false }
            && access.Ready(candidate, expectedModel);
    }

    public void Interrupt()
    {
        remaining = IntervalMs;
        if (lease is { } frozen && paused(frozen.Actor)) return;
        if (lease is { } owned && active is { } timeline && Valid(owned, out var currentModel) && currentModel == model)
            access.Stop(owned, model, timeline);
        active = null;
    }

    // Used by the existing single-action player, which retains manual-action priority.
    public SkillAnimation PrepareSingle(LocalActorLease current, long now)
    {
        if (!Valid(current, out var expectedModel)) throw new InvalidOperationException("请等待本地演示模型完成加载。");
        if (paused(current.Actor)) throw new InvalidOperationException("请先恢复动作，再试播平A。");
        if (busy(current.Actor, now)) throw new InvalidOperationException("请等手动技能或连招结束，再试播平A。");
        if (!access.Prepare(current, expectedModel, !rotationLocked(current.Actor)))
            throw new InvalidOperationException("本地玩家或演示模型暂不可用。");
        return Animation(Variant);
    }

    public void Tick(LocalActorLease? current, bool battle, long now)
    {
        if (current is null || !Valid(current, out var expectedModel)) { ForgetScene(); return; }
        if (lease != current || model != expectedModel)
        {
            Interrupt();
            lease = current; model = expectedModel; remaining = 0; lastTick = null; wasPaused = false;
        }
        var elapsed = lastTick is { } previous ? Math.Max(0, now - previous) : 0;
        lastTick = now;
        if (paused(current.Actor)) { wasPaused = true; return; }
        if (wasPaused) { elapsed = 0; wasPaused = false; }
        if (busy(current.Actor, now)) { Interrupt(); return; }
        if (!Enabled || !battle) { Interrupt(); remaining = 0; return; }
        remaining = Math.Max(0, remaining - elapsed);
        if (remaining > 0) return;
        // Missed frames never accumulate attacks. Take a fresh player position per swing.
        Interrupt();
        if (!access.Prepare(current, model, !rotationLocked(current.Actor))) return;
        var animation = Animation(Variant);
        access.Play(current, model, animation.TimelineId);
        active = animation.TimelineId;
    }

    // Scene/nonce loss must not perform cleanup writes through an old lease.
    public void ForgetScene() { lease = null; active = null; lastTick = null; remaining = 0; wasPaused = false; }
}

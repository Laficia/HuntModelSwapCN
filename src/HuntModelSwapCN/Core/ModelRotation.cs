namespace HuntModelSwapCN.Core;

// Only controls the local rendered heading of tracked demo actors.
public sealed class ModelRotation(SwapSession session, IDirectionalAnimationAccess access,
    Func<ActorIdentity, bool>? isEligible = null)
{
    private sealed record Entry(ActorIdentity Actor, int ModelId, float Radians);
    private readonly Dictionary<ActorIdentity, Entry> entries = [];
    public IReadOnlyCollection<ActorIdentity> Actors => entries.Keys;
    public static float Degrees(float radians) => ((radians * 180 / MathF.PI) % 360 + 360) % 360;
    private bool Valid(Entry entry) => session.EntryFor(entry.Actor) is { WaitingForDraw: false, Restoring: false } swap
        && swap.Applied.ModelId == entry.ModelId
        && (isEligible?.Invoke(entry.Actor) ?? true);

    public void Set(ActorIdentity actor, float degrees)
    {
        if (!float.IsFinite(degrees)) throw new InvalidOperationException("旋转角度必须是有限数值。");
        var applied = session.EntryFor(actor)
            ?? throw new InvalidOperationException("请先生成预览或替换目标模型。");
        var entry = new Entry(actor, applied.Applied.ModelId, MathF.IEEERemainder(degrees, 360) * MathF.PI / 180);
        if (!Valid(entry)) throw new InvalidOperationException("请等待演示对象完成加载。");
        access.Face(actor, entry.ModelId, entry.Radians);
        entries[actor] = entry;
    }

    public void Release(ActorIdentity actor) => entries.Remove(actor);
    public void Clear() => entries.Clear();
    public void Tick()
    {
        foreach (var entry in entries.Values.ToArray())
        {
            if (!Valid(entry)) { entries.Remove(entry.Actor); continue; }
            try { access.Face(entry.Actor, entry.ModelId, entry.Radians); }
            catch { entries.Remove(entry.Actor); throw; }
        }
    }
}

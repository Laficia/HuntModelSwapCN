namespace HuntModelSwapCN.Core;

public interface IDialogueAccess
{
    void OpenBubble(ActorIdentity actor, int modelId, string text, float seconds);
    void CloseBubble(ActorIdentity actor, int modelId, string expectedText);
}

public sealed record DialogueStep(string Text, float Seconds, int WaitAfterMs);

// Framework-thread only. Never reads network packets, sends chat, or casts an action.
public sealed class DialoguePreview(SwapSession session, IDialogueAccess access,
    Func<ActorIdentity, bool>? isEligible = null)
{
    private sealed record Owned(ActorIdentity Actor, int ModelId, string Name, string Text);
    private sealed record Run(Owned Owner, DialogueStep[] Steps, int Next, long Due);
    private readonly Dictionary<ActorIdentity, Owned> active = [];
    private Run? run;
    public bool Running => run is not null;
    private bool Valid(Owned owner) => session.Entries.Any(e => e.Actor == owner.Actor
        && e.Applied.ModelId == owner.ModelId && e.ReplacementName == owner.Name && !e.WaitingForDraw && !e.Restoring)
        && (isEligible?.Invoke(owner.Actor) ?? true);

    public void Play(ActorIdentity actor, string text, float seconds)
    {
        text = Catalog.HuntDialogueCatalog.Normalize(text);
        if (!float.IsFinite(seconds) || seconds is < 1 or > 15) throw new InvalidOperationException("气泡时长必须为 1～15 秒。");
        var entry = session.Entries.FirstOrDefault(e => e.Actor == actor)
            ?? throw new InvalidOperationException("请先生成预览或替换目标模型。");
        var owner = new Owned(actor, entry.Applied.ModelId, entry.ReplacementName, text);
        if (!Valid(owner)) throw new InvalidOperationException("请等待演示对象完成加载。");
        access.OpenBubble(actor, owner.ModelId, text, seconds);
        active[actor] = owner;
    }

    public void Start(ActorIdentity actor, DialogueStep[] steps, long now)
    {
        if (steps.Length is < 1 or > 32 || steps.Any(s => !float.IsFinite(s.Seconds) || s.Seconds is < 1 or > 15
            || s.WaitAfterMs is < 1000 or > 60000)) throw new InvalidOperationException("台词序列无效。");
        foreach (var step in steps) Catalog.HuntDialogueCatalog.Normalize(step.Text);
        StopSequence();
        Play(actor, steps[0].Text, steps[0].Seconds);
        if (steps.Length > 1) run = new(active[actor], steps.ToArray(), 1, now + steps[0].WaitAfterMs);
    }

    public void Tick(long now)
    {
        foreach (var owner in active.Values.ToArray())
            if (!Valid(owner)) active.Remove(owner.Actor);
        if (run is not { } pending) return;
        if (!Valid(pending.Owner)) { run = null; return; }
        if (now < pending.Due) return;
        // At most one line per frame; a delayed update never bursts several lines at once.
        run = null;
        var step = pending.Steps[pending.Next];
        Play(pending.Owner.Actor, step.Text, step.Seconds);
        if (pending.Next + 1 < pending.Steps.Length)
            run = pending with { Next = pending.Next + 1, Due = now + step.WaitAfterMs };
    }

    public void StopSequence() => run = null;
    public void Clear(ActorIdentity actor)
    {
        if (run?.Owner.Actor == actor) run = null;
        if (!active.Remove(actor, out var owner) || !Valid(owner)) return;
        access.CloseBubble(actor, owner.ModelId, owner.Text);
    }
    public void Reset()
    {
        run = null;
        List<Exception> failures = [];
        foreach (var actor in active.Keys.ToArray())
            try { Clear(actor); } catch (Exception ex) { failures.Add(ex); }
        if (failures.Count > 0) throw new AggregateException("关闭演示气泡失败。", failures);
    }
    public void ForgetScene() { run = null; active.Clear(); }
}

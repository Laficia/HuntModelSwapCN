namespace HuntModelSwapCN.Core;

// Durations are preview pacing in active milliseconds. A stalled frame advances at most one clip.
public sealed class KnockdownPreview(SwapSession session, IAnimationAccess animation,
    Func<ActorIdentity, int, bool> ready, Func<ActorIdentity, bool> paused, Action<ActorIdentity, int> restoreIdle)
{
    public readonly record struct Step(ushort Timeline, int MinimumMs = 750, int MaximumMs = 3500);
    private static readonly Step[] Ground = [new(72), new(73, 3000, 3000)];
    private static readonly Step[] Human = [new(66), new(67, 3000, 3000)];
    private static readonly Step[] Flying = [new(74), new(75, 600, 600), new(76), new(73, 3000, 3000)];
    // This skeleton has the landing clip but has no separate prone pose.
    private static readonly Step[] FlyingLanding = [new(74), new(75, 600, 600), new(76, 3500, 3500)];
    private static readonly Step Rise = new(77);

    public static IReadOnlyList<Step> Steps(string family) => family switch
    {
        "" => Human,
        "m0129" => FlyingLanding,
        "m0007" or "m0011" or "m0016" or "m0025" or "m0064" or "m0069" or "m0118" or "m0137" or
        "m0138" or "m0182" or "m0195" or "m0198" or "m0234" or "m0296" or "m0333" or "m0380" or "m0871" => Flying,
        _ => Ground,
    };

    private sealed class Run(int model, IReadOnlyList<Step> steps, bool canRise, long now)
    {
        public readonly int Model = model;
        public readonly IReadOnlyList<Step> Steps = steps;
        public readonly bool CanRise = canRise;
        public int Index;
        public bool Rising;
        public Step Current => Rising ? Rise : Steps[Index];
        public long LastTick = now;
        public long Elapsed;
        public bool WasPaused;
    }
    private readonly Dictionary<ActorIdentity, Run> runs = [];
    public IEnumerable<ActorIdentity> Actors => runs.Keys;
    public bool Active(ActorIdentity actor) => runs.ContainsKey(actor);
    private bool Valid(ActorIdentity actor, int model) =>
        session.EntryFor(actor) is { WaitingForDraw: false, Restoring: false, PresentationOnly: false } entry
        && entry.Applied.ModelId == model && ready(actor, model);

    public void Start(ActorIdentity actor, string family, bool canRise, long now, Action interrupt)
    {
        if (Active(actor)) throw new InvalidOperationException("该模型已在演示击倒。");
        if (session.EntryFor(actor) is not { } entry || !Valid(actor, entry.Applied.ModelId))
            throw new InvalidOperationException("请先生成或替换模型，并等待模型加载完成。");
        if (paused(actor)) throw new InvalidOperationException("请先继续动作，再演示击倒。");
        interrupt();
        var run = new Run(entry.Applied.ModelId, Steps(family), canRise, now);
        animation.PlayAnimation(actor, run.Model, run.Current.Timeline);
        runs.Add(actor, run);
    }

    public void Recover(ActorIdentity actor, long now)
    {
        if (!runs.TryGetValue(actor, out var run)) return;
        if (!Valid(actor, run.Model)) { runs.Remove(actor); return; }
        if (!run.CanRise || run.Rising) { Cancel(actor); return; }
        animation.PlayAnimation(actor, run.Model, Rise.Timeline);
        run.Rising = true;
        run.Elapsed = 0;
        run.LastTick = now;
        run.WasPaused = false;
    }

    public void Tick(long now)
    {
        foreach (var (actor, run) in runs.ToArray())
        {
            if (!Valid(actor, run.Model)) { runs.Remove(actor); continue; }
            var elapsed = Math.Max(0, now - run.LastTick);
            run.LastTick = now;
            if (paused(actor)) { run.WasPaused = true; continue; }
            if (!run.WasPaused) run.Elapsed += elapsed;
            run.WasPaused = false;
            var step = run.Current;
            if (run.Elapsed < step.MinimumMs) continue;
            try
            {
                if (run.Elapsed < step.MaximumMs && animation.IsAnimationActive(actor, run.Model, step.Timeline)) continue;
                if (run.Rising || run.Index + 1 == run.Steps.Count) { Recover(actor, now); continue; }
                animation.PlayAnimation(actor, run.Model, run.Steps[run.Index + 1].Timeline);
                run.Index++;
                run.Elapsed = 0;
            }
            catch { Cancel(actor); throw; }
        }
    }

    public void Cancel(ActorIdentity actor)
    {
        if (!runs.Remove(actor, out var run) || !Valid(actor, run.Model)) return;
        animation.StopAnimation(actor, run.Model, run.Current.Timeline);
        restoreIdle(actor, run.Model);
    }

    public void ForgetScene() => runs.Clear();
}

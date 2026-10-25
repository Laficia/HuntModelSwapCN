using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public interface ILinkedDummyAccess
{
    bool Exists(ActorIdentity source);
    LocalPlacement Placement(ActorIdentity source);
    bool IsHidden(ActorIdentity source);
    void Hide(ActorIdentity source);
    void RestoreVisibility(ActorIdentity source, bool hidden);
    void Align(LocalActorLease visual, ActorIdentity source);
    bool IsReady(LocalActorLease visual);
    void ForgetScene() { }
}

// A Type=0 furnishing has layout graphics, not a CharacterBase. Reuse the one
// client-only preview at its position while preserving the real training target.
public sealed class LinkedDummyPreview(SwapSession session, LocalPreview preview, IActorAccess actors, ILinkedDummyAccess access)
{
    public ActorIdentity? Source { get; private set; }
    public ActorIdentity? Visual { get; private set; }
    private bool originalHidden;
    private long startedAt;
    private bool ready;
    private readonly Dictionary<ActorIdentity, bool> pendingVisibility = [];

    public bool Contains(ActorIdentity actor) => Source == actor || Visual == actor;
    public bool Replaces(ActorIdentity actor) => Source == actor && Visual is { } visual && preview.OwnsLive(visual);

    public void Start(ActorIdentity source, string originalName, HuntAppearance appearance, float scale, long now)
    {
        if (Source.HasValue) throw new InvalidOperationException("请先恢复已有的庭具木人替换。");
        if (!access.Exists(source)) throw new InvalidOperationException("木人已离开加载范围。");
        var placement = access.Placement(source) with { Targetable = true };
        placement.Validate(); ModelSize.Validate(scale);
        var current = actors.Read(source);
        var applied = HuntAppearanceReplacement.Create(current, appearance, scale);
        var wasHidden = pendingVisibility.TryGetValue(source, out var savedHidden) ? savedHidden : access.IsHidden(source);
        ActorIdentity visual;
        try { visual = preview.SpawnAt(appearance, scale, placement); }
        catch { access.RestoreVisibility(source, wasHidden); throw; }
        pendingVisibility.Remove(source);
        Source = source; Visual = visual; originalHidden = wasHidden; startedAt = now; ready = false;
        try
        {
            session.Apply(source, originalName, appearance.Name, applied);
            access.Hide(source);
        }
        catch { Clear(); throw; }
    }

    public void Tick(long now)
    {
        FinishRestores();
        if (Source is not { } source || Visual is not { } visual) return;
        if (!access.Exists(source) || !preview.OwnsLive(visual)) { Clear(); return; }
        var lease = preview.Actor!;
        ready |= access.IsReady(lease);
        if (!ready && now - startedAt >= 5000)
        {
            Clear();
            throw new InvalidOperationException("庭具木人的本地替换模型未能在 5 秒内完成加载，已移除替身并请求还原木人。");
        }
        access.Hide(source);
        access.Align(lease, source);
    }

    public void Clear()
    {
        if (Source is not { } source) return;
        // A failed delete retains the link for retry instead of losing ownership.
        if (Visual is { } visual && preview.Owns(visual)) preview.Clear();
        Source = null; Visual = null;
        pendingVisibility[source] = originalHidden;
        session.Restore(source);
    }

    public void FinishRestores()
    {
        foreach (var (source, hidden) in pendingVisibility.ToArray())
        {
            if (session.Entries.Any(e => e.Actor == source && e.Restoring)) continue;
            if (access.Exists(source)) access.RestoreVisibility(source, hidden);
            pendingVisibility.Remove(source);
        }
    }

    public void ForgetScene() { Source = null; Visual = null; pendingVisibility.Clear(); access.ForgetScene(); }
}

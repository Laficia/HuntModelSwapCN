namespace HuntModelSwapCN.Core;

public interface IModelVisibilityAccess
{
    bool Exists(ActorIdentity actor);
    bool IsHidden(ActorIdentity actor);
    void SetHidden(ActorIdentity actor, bool hidden);
}

public sealed class ModelVisibility(IModelVisibilityAccess access)
{
    private readonly Dictionary<ActorIdentity, bool> originals = [];
    private readonly HashSet<ActorIdentity> selected = [];
    public bool HideSelf { get; private set; }
    public bool Active => HideSelf || selected.Count > 0;
    public int Count => originals.Count;
    public IEnumerable<ActorIdentity> ManagedActors => selected.Union(originals.Keys);

    public void SetHideSelf(bool self) => HideSelf = self;
    public void Hide(ActorIdentity actor) => selected.Add(actor);
    public void Show(ActorIdentity actor) => selected.Remove(actor);

    public void Tick(ActorIdentity? self, IReadOnlySet<ActorIdentity> redrawingActors)
    {
        if (!Active && originals.Count == 0) return;
        selected.RemoveWhere(actor => !access.Exists(actor));
        var wanted = selected.ToHashSet();
        if (HideSelf && self is { } player) wanted.Add(player);
        foreach (var actor in originals.Keys.ToArray())
        {
            if (!access.Exists(actor)) { originals.Remove(actor); continue; }
            if (!wanted.Contains(actor) && !redrawingActors.Contains(actor)) Restore(actor);
        }
        foreach (var actor in wanted)
        {
            // DisableDraw during a swap is temporary, not the object's original hidden state.
            if (redrawingActors.Contains(actor) || !access.Exists(actor)) continue;
            if (!originals.ContainsKey(actor)) originals.Add(actor, access.IsHidden(actor));
            access.SetHidden(actor, true);
        }
    }

    private void Restore(ActorIdentity actor)
    {
        if (access.Exists(actor)) access.SetHidden(actor, originals[actor]);
        originals.Remove(actor);
    }

    public void Reset()
    {
        HideSelf = false;
        selected.Clear();
        List<Exception> failures = [];
        foreach (var actor in originals.Keys.ToArray())
            try { Restore(actor); } catch (Exception ex) { failures.Add(ex); }
        if (failures.Count > 0) throw new AggregateException("部分模型显示状态恢复失败。", failures);
    }

    // The old scene's objects no longer belong to this session.
    public void ForgetScene() { originals.Clear(); selected.Clear(); HideSelf = false; }
}

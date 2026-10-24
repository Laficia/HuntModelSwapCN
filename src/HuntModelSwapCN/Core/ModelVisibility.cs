namespace HuntModelSwapCN.Core;

public readonly record struct VisibilityCandidate(ActorIdentity Actor, bool IsSelf, bool IsMonster);
public interface IModelVisibilityAccess
{
    bool Exists(ActorIdentity actor);
    bool IsHidden(ActorIdentity actor);
    void SetHidden(ActorIdentity actor, bool hidden);
}

public sealed class ModelVisibility(IModelVisibilityAccess access)
{
    private readonly Dictionary<ActorIdentity, bool> originals = [];
    public bool HideSelf { get; private set; }
    public bool HideMonsters { get; private set; }
    public bool Active => HideSelf || HideMonsters;
    public int Count => originals.Count;

    public void SetOptions(bool self, bool monsters) { HideSelf = self; HideMonsters = monsters; }

    public void Tick(IEnumerable<VisibilityCandidate> candidates, IReadOnlySet<ActorIdentity> protectedActors)
    {
        var wanted = candidates.Where(c => (c.IsSelf && HideSelf || c.IsMonster && HideMonsters)
            && !protectedActors.Contains(c.Actor)).Select(c => c.Actor).ToHashSet();
        foreach (var actor in originals.Keys.ToArray())
        {
            if (!access.Exists(actor)) { originals.Remove(actor); continue; }
            if (!wanted.Contains(actor)) Restore(actor);
        }
        foreach (var actor in wanted)
        {
            if (!access.Exists(actor)) continue;
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
        SetOptions(false, false);
        List<Exception> failures = [];
        foreach (var actor in originals.Keys.ToArray())
            try { Restore(actor); } catch (Exception ex) { failures.Add(ex); }
        if (failures.Count > 0) throw new AggregateException("部分模型显示状态恢复失败。", failures);
    }

    // The old scene's objects no longer belong to this session.
    public void ForgetScene() { originals.Clear(); SetOptions(false, false); }
}

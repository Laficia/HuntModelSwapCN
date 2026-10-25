namespace HuntModelSwapCN.Core;

public sealed record DemoDefault(int ModelId, float Heading, float Scale);

public sealed class DemoDefaults
{
    private readonly Dictionary<ActorIdentity, DemoDefault> values = [];
    public void Remember(ActorIdentity actor, int modelId, float heading, float scale)
    {
        ModelSize.Validate(scale);
        if (!float.IsFinite(heading)) throw new InvalidOperationException("初始朝向无效。");
        if (!values.TryGetValue(actor, out var current) || current.ModelId != modelId)
            values[actor] = new(modelId, heading, scale);
        else values[actor] = current with { Scale = scale };
    }
    public DemoDefault? For(ActorIdentity actor, int modelId)
        => values.TryGetValue(actor, out var value) && value.ModelId == modelId ? value : null;
    public void Forget(ActorIdentity actor) => values.Remove(actor);
    public void Prune(IReadOnlyCollection<SwapEntry> entries)
    {
        foreach (var actor in values.Keys.ToArray())
            if (!entries.Any(e => e.Actor == actor && !e.Restoring && e.Applied.ModelId == values[actor].ModelId)) values.Remove(actor);
    }
    public void Clear() => values.Clear();
}

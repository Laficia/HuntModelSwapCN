namespace HuntModelSwapCN.Core;

// Address is an identity check only; every native access must re-resolve the object table.
public readonly record struct ActorIdentity(ushort Index, ulong ObjectId, uint DataId, uint NameId, nint Address);
public sealed record VisualState(int ModelId, int SecondaryModelId, float ModelScale,
    byte[] Customize, ulong[] Equipment, ulong[] Weapons, byte Flags1, byte Flags2,
    byte[] NameBytes, byte Level, float ObjectScale, System.Numerics.Vector3 DrawScale, byte NameplateIcon = 0);
public static class ModelSize
{
    public static void Validate(float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException("体型倍率必须是大于 0 的有限数值。");
    }

    // This is the model's actual scale, not a multiplier applied to the source or target NPC.
    public static VisualState Apply(VisualState state, float scale)
    {
        Validate(scale);
        return state with { ModelScale = scale, ObjectScale = scale, DrawScale = new(scale) };
    }
}
public interface IActorAccess
{
    bool Exists(ActorIdentity actor);
    VisualState Read(ActorIdentity actor);
    void BeginRedraw(ActorIdentity actor, VisualState state);
    void FinishRedraw(ActorIdentity actor);
    void RefreshPresentation(ActorIdentity actor, VisualState state);
    bool NeedsRedraw(ActorIdentity actor, VisualState state) => false;
}
public sealed record SwapEntry(ActorIdentity Actor, string OriginalName, string ReplacementName,
    VisualState Original, VisualState Applied, bool WaitingForDraw, bool Restoring, bool PresentationOnly = false);

public sealed class SwapSession(IActorAccess access)
{
    private readonly Dictionary<ActorIdentity, SwapEntry> entries = new();
    public IReadOnlyCollection<SwapEntry> Entries => entries.Values;

    public string? NameFor(ActorIdentity actor) => entries.TryGetValue(actor, out var entry)
        ? entry.Restoring ? entry.OriginalName : entry.ReplacementName : null;

    public void Apply(ActorIdentity actor, string originalName, string replacementName, VisualState state, bool presentationOnly = false)
    {
        if (!access.Exists(actor)) throw new InvalidOperationException("目标已离开加载范围，请重新选择。");
        var original = entries.TryGetValue(actor, out var previous) ? previous.Original : access.Read(actor);
        // Register before writing so a partial failure can still be restored.
        entries[actor] = new(actor, previous?.OriginalName ?? originalName, replacementName, original, state, true, false, presentationOnly);
        if (presentationOnly) access.RefreshPresentation(actor, state);
        else access.BeginRedraw(actor, state);
    }

    public bool Restore(ActorIdentity actor)
    {
        if (!entries.TryGetValue(actor, out var entry)) return false;
        if (!access.Exists(actor)) { entries.Remove(actor); return false; }
        entries[actor] = entry with { WaitingForDraw = true, Restoring = true };
        if (entry.PresentationOnly) access.RefreshPresentation(actor, entry.Original);
        else access.BeginRedraw(actor, entry.Original);
        return true;
    }

    public void Resize(ActorIdentity actor, float scale)
    {
        ModelSize.Validate(scale);
        if (!entries.TryGetValue(actor, out var entry) || !access.Exists(actor))
            throw new InvalidOperationException("请先生成预览或替换当前目标。");
        if (entry.PresentationOnly)
            throw new InvalidOperationException("请调整原位替身的体型，真实木人的体型保持原样。");
        if (entry.WaitingForDraw || entry.Restoring)
            throw new InvalidOperationException("请等待对象完成重绘后再调整体型。");
        var state = ModelSize.Apply(entry.Applied, scale);
        entries[actor] = entry with { Applied = state };
        access.RefreshPresentation(actor, state);
    }

    public void Tick()
    {
        foreach (var entry in entries.Values.ToArray())
        {
            if (!access.Exists(entry.Actor)) { entries.Remove(entry.Actor); continue; }
            if (entry.WaitingForDraw)
            {
                if (!entry.PresentationOnly) access.FinishRedraw(entry.Actor);
                if (entry.Restoring) { entries.Remove(entry.Actor); continue; }
                entries[entry.Actor] = entry with { WaitingForDraw = false };
            }
            // A real dummy can be reset without leaving its object-table slot.
            // Reuse the two-frame redraw, keeping the first original snapshot.
            if (!entry.PresentationOnly && access.NeedsRedraw(entry.Actor, entry.Applied))
            {
                entries[entry.Actor] = entry with { WaitingForDraw = true };
                access.BeginRedraw(entry.Actor, entry.Applied);
                continue;
            }
            access.RefreshPresentation(entry.Actor, entry.Applied);
        }
    }

    public void RestoreAll()
    {
        foreach (var actor in entries.Keys.ToArray()) Restore(actor);
    }

    // Used only after the game has discarded the territory's actors.
    public void ForgetDespawned() => entries.Clear();

    public void Forget(ActorIdentity actor) => entries.Remove(actor);

    public void Shutdown()
    {
        List<Exception> failures = [];
        foreach (var entry in entries.Values.ToArray())
        {
            if (!access.Exists(entry.Actor)) continue;
            if (entry.PresentationOnly)
            {
                try { access.RefreshPresentation(entry.Actor, entry.Original); }
                catch (Exception ex) { failures.Add(ex); }
                continue;
            }
            try { access.BeginRedraw(entry.Actor, entry.Original); }
            catch (Exception ex) { failures.Add(ex); }
            finally
            {
                try { access.FinishRedraw(entry.Actor); }
                catch (Exception ex) { failures.Add(ex); }
            }
        }
        entries.Clear();
        if (failures.Count != 0) throw new AggregateException("部分对象恢复失败。", failures);
    }
}

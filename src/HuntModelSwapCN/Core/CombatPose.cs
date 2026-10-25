namespace HuntModelSwapCN.Core;

public enum ModelStance { Automatic, Battle, Idle }

// One controller for the current owned local actor, including front previews.
public sealed class CombatPose
{
    public ModelStance Mode { get; private set; }
    private LocalActorLease? visual;
    private int modelId;
    private CombatPresentation? applied;
    private bool resetRequested;

    public void SetMode(ModelStance mode)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidOperationException("姿态选项无效。");
        if (Mode == mode) return;
        Mode = mode;
        resetRequested = true;
    }

    public void Reset() { Mode = ModelStance.Automatic; resetRequested = true; }
    public void Forget() { visual = null; applied = null; resetRequested = false; }

    public void Tick(LocalActorLease lease, int expectedModelId, CombatPresentation source, bool ready, bool deferred,
        Action<LocalActorLease, CombatPresentation, bool> write)
    {
        if (visual != lease || modelId != expectedModelId)
        {
            visual = lease; modelId = expectedModelId; applied = null;
        }
        if (!ready) { applied = null; return; }
        if (deferred) return;
        var desired = Mode switch {
            ModelStance.Battle => new CombatPresentation(true, true),
            ModelStance.Idle => new CombatPresentation(false, false),
            _ => source,
        };
        var change = resetRequested || (applied is { } previous ? previous.WeaponDrawn != desired.WeaponDrawn
            : Mode != ModelStance.Automatic || desired.WeaponDrawn);
        write(lease, desired, change);
        applied = desired;
        resetRequested = false;
    }
}

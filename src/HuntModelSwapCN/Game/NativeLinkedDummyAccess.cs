using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeLinkedDummyAccess(NativeActorAccess actors, NativeLocalActorAccess local) : ILinkedDummyAccess
{
    private sealed record LayoutState(nint Address, bool Active, bool Collider);
    private readonly Dictionary<ActorIdentity, LayoutState> layouts = [];
    public bool Exists(ActorIdentity actor)
    {
        var exists = actors.Exists(actor);
        if (!exists) layouts.Remove(actor);
        return exists;
    }
    private NativeCharacter* Character(ActorIdentity actor)
        => actors.Resolve(actor) is { } npc ? (NativeCharacter*)npc.Address
            : throw new InvalidOperationException("庭具木人已离开加载范围。");
    public LocalPlacement Placement(ActorIdentity source)
    {
        var c = Character(source);
        var group = c->GameObject.SharedGroupLayoutInstance;
        if (group != null && !layouts.ContainsKey(source))
            layouts[source] = new((nint)group, group->WantToBeActive(), group->IsColliderActive());
        return new(c->GameObject.Position, c->GameObject.Rotation, false);
    }
    public bool IsHidden(ActorIdentity source) => ModelVisibilityCodec.IsHidden(&Character(source)->GameObject);
    public void Hide(ActorIdentity source)
    {
        var c = Character(source);
        // Conceal layout graphics only. DisableDraw clears IsDrawn, which makes
        // GetIsTargetable reject the real furnishing even after SetHardTarget.
        var group = c->GameObject.SharedGroupLayoutInstance;
        if (group != null && layouts.TryGetValue(source, out var original) && original.Address == (nint)group)
        {
            group->SetActive(false);
            group->SetColliderActive(original.Collider);
        }
    }
    public void RestoreVisibility(ActorIdentity source, bool hidden)
    {
        var c = Character(source);
        ModelVisibilityCodec.SetHidden(&c->GameObject, hidden);
        var group = c->GameObject.SharedGroupLayoutInstance;
        if (layouts.Remove(source, out var original) && group != null && original.Address == (nint)group)
        {
            group->SetActive(original.Active);
            group->SetColliderActive(original.Collider);
        }
    }
    public void Align(LocalActorLease visual, ActorIdentity source)
    {
        if (!local.Exists(visual)) return;
        var position = Character(source)->GameObject.Position;
        new LocalPlacement(position, 0).Validate();
        var c = Character(visual.Actor);
        if ((System.Numerics.Vector3)c->GameObject.Position != (System.Numerics.Vector3)position)
            c->GameObject.SetPosition(position.X, position.Y, position.Z);
        var targetStatus = Character(source)->GameObject.CurrentTargetStatus;
        if (c->GameObject.CurrentTargetStatus != targetStatus) c->GameObject.SetTargetStatus(targetStatus);
    }
    public bool IsReady(LocalActorLease visual)
    {
        if (!local.Exists(visual)) return false;
        var draw = Character(visual.Actor)->GameObject.DrawObject;
        return draw != null && draw->LoadState == 3;
    }
    public CombatPresentation ReadCombat(ActorIdentity source)
    {
        var c = Character(source);
        return CombatPresentation.From(c->InCombat, c->IsWeaponDrawn);
    }
    public void SyncCombat(LocalActorLease visual, CombatPresentation combat, bool changePose)
    {
        if (!local.Exists(visual)) return;
        var c = Character(visual.Actor);
        if (c->GameObject.DrawObject == null || c->GameObject.DrawObject->LoadState != 3) return;
        NativeCombatPresentationCodec.Write(c, combat);
        // Only enter/leave battle idle on a transition. Never restart the base
        // timeline every frame or copy a furnishing's incompatible animation IDs.
        if (changePose) c->Timeline.TimelineSequencer.PlayTimeline(combat.WeaponDrawn
            ? NativeCombatPresentationCodec.BattleIdle : NativeCombatPresentationCodec.NormalIdle);
    }
    public void ForgetScene() => layouts.Clear();
}

using System.Numerics;
using Dalamud.Plugin.Services;
using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeAutoAttackAccess(IObjectTable objects, NativeLocalActorAccess local,
    NativeActorAccess actors) : IAutoAttackAccess
{
    public bool Ready(LocalActorLease lease, int expectedModelId)
    {
        if (!local.Exists(lease) || actors.Resolve(lease.Actor) is not { } npc) return false;
        var c = (NativeCharacter*)npc.Address;
        return c->ModelContainer.ModelCharaId == expectedModelId && c->GameObject.DrawObject != null
            && c->GameObject.DrawObject->LoadState == 3;
    }

    public bool Prepare(LocalActorLease lease, int expectedModelId, bool facePlayer)
    {
        if (!Ready(lease, expectedModelId) || objects.LocalPlayer is not { } player || !player.IsValid()
            || actors.Resolve(lease.Actor) is not { } visual) return false;
        var delta = player.Position - visual.Position;
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || !float.IsFinite(delta.Z)) return false;
        if (facePlayer && new Vector2(delta.X, delta.Z).LengthSquared() > 0.0001f)
            actors.Face(lease.Actor, expectedModelId, MathF.Atan2(delta.X, delta.Z));
        return true;
    }

    public void Play(LocalActorLease lease, int expectedModelId, ushort timeline)
    {
        if (Ready(lease, expectedModelId)) actors.PlayAnimation(lease.Actor, expectedModelId, timeline);
    }

    public void Stop(LocalActorLease lease, int expectedModelId, ushort timeline)
    {
        if (Ready(lease, expectedModelId)) actors.StopAnimation(lease.Actor, expectedModelId, timeline);
    }
}

using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using HuntModelSwapCN.Core;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeModelVisibility(IObjectTable objects) : IModelVisibilityAccess
{
    private static ActorIdentity Identity(IGameObject actor) => new(actor.ObjectIndex, actor.GameObjectId,
        actor.BaseId, actor is IBattleNpc npc ? npc.NameId : 0, actor.Address);

    public ActorIdentity? Self => objects.LocalPlayer is { } player && player.IsValid() && player.Address != 0
        ? Identity(player) : null;

    public string Name(ActorIdentity actor) => Resolve(actor)?.Name.ToString() ?? "已离开加载范围";

    public IEnumerable<ActorIdentity> BulkCandidates(IReadOnlySet<ActorIdentity> protectedActors)
    {
        foreach (var candidate in objects)
        {
            if (candidate is not IBattleNpc npc || !npc.IsValid() || npc.Address == 0) continue;
            var actor = Identity(npc);
            if (!protectedActors.Contains(actor) && CanHide(actor) is null) yield return actor;
        }
    }

    public string? CanHide(ActorIdentity actor)
    {
        if (Resolve(actor) is not IBattleNpc npc) return "请先选中一只战斗小怪（主动或被动均可）。";
        return ModelVisibilityCodec.CannotHideSelected((GameObject*)npc.Address);
    }

    private IGameObject? Resolve(ActorIdentity actor)
    {
        if (actor.Index >= objects.Length || objects[actor.Index] is not { } current || !current.IsValid()
            || current.Address == 0 || Identity(current) != actor) return null;
        return current;
    }
    public bool Exists(ActorIdentity actor) => Resolve(actor) != null;
    public bool IsHidden(ActorIdentity actor)
    {
        var pointer = (GameObject*)(Resolve(actor)?.Address ?? 0);
        return pointer != null && ModelVisibilityCodec.IsHidden(pointer);
    }
    public void SetHidden(ActorIdentity actor, bool hidden)
    {
        var pointer = (GameObject*)(Resolve(actor)?.Address ?? 0);
        if (pointer != null) ModelVisibilityCodec.SetHidden(pointer, hidden);
    }
}

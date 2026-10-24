using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using HuntModelSwapCN.Core;
using NpcKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeModelVisibility(IObjectTable objects) : IModelVisibilityAccess
{
    private static ActorIdentity Identity(IGameObject actor) => new(actor.ObjectIndex, actor.GameObjectId,
        actor.BaseId, actor is IBattleNpc npc ? npc.NameId : 0, actor.Address);

    public IReadOnlyList<VisibilityCandidate> Candidates()
    {
        List<VisibilityCandidate> result = [];
        var self = objects.LocalPlayer?.Address ?? 0;
        foreach (var actor in objects)
        {
            if (!actor.IsValid() || actor.Address == 0) continue;
            var isSelf = self != 0 && actor.Address == self;
            var monster = actor is IBattleNpc npc && npc.BattleNpcKind == NpcKind.Combatant
                && npc.OwnerId is 0 or 0xe0000000
                && ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)npc.Address)->IsHostile;
            if (isSelf || monster) result.Add(new(Identity(actor), isSelf, monster));
        }
        return result;
    }

    private GameObject* Resolve(ActorIdentity actor)
    {
        if (actor.Index >= objects.Length || objects[actor.Index] is not { } current || !current.IsValid()
            || current.Address == 0 || Identity(current) != actor) return null;
        return (GameObject*)current.Address;
    }
    public bool Exists(ActorIdentity actor) => Resolve(actor) != null;
    public bool IsHidden(ActorIdentity actor)
    {
        var pointer = Resolve(actor);
        return pointer != null && ModelVisibilityCodec.IsHidden(pointer);
    }
    public void SetHidden(ActorIdentity actor, bool hidden)
    {
        var pointer = Resolve(actor);
        if (pointer != null) ModelVisibilityCodec.SetHidden(pointer, hidden);
    }
}

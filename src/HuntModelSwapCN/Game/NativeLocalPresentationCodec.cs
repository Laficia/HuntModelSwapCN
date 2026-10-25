using FFXIVClientStructs.FFXIV.Client.Game.Object;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

// Only for a client-only actor owned by LocalPreview. Never applied to a real game NPC.
public static unsafe class NativeLocalPresentationCodec
{
    public static bool Write(NativeCharacter* c, byte battalion, bool aggressive)
    {
        if (c->IsHostile == aggressive && c->Battalion == battalion) return false;
        // Battalion determines enemy relationship; IsHostile selects aggressive/passive icon color.
        c->IsHostile = aggressive;
        c->Battalion = battalion;
        c->GameObject.UpdateFlags |= ObjectUpdateFlags.Name;
        return true;
    }
}

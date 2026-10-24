using FFXIVClientStructs.FFXIV.Client.Game.Object;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

// Only for a client-only actor owned by LocalPreview. Never applied to a real game NPC.
public static unsafe class NativeLocalPresentationCodec
{
    public static bool Write(NativeCharacter* c, byte battalion)
    {
        if (c->IsHostile && c->Battalion == battalion) return false;
        // Enemy nameplate color/type depends on the battalion relationship as well as this flag.
        c->IsHostile = true;
        c->Battalion = battalion;
        c->GameObject.UpdateFlags |= ObjectUpdateFlags.Name;
        return true;
    }
}

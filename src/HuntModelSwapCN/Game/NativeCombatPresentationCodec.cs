using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

// Call only after validating the current LocalPreview lease, never on the source.
public static unsafe class NativeCombatPresentationCodec
{
    public const ushort BattleIdle = 34;
    public const ushort NormalIdle = 3;

    public static void Write(NativeCharacter* visual, CombatPresentation combat)
    {
        // Use the generated bitfield setters; preserve aggression and other flags.
        visual->InCombat = combat.InCombat;
        visual->Timeline.IsWeaponDrawn = combat.WeaponDrawn;
    }
}

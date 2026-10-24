using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

public static unsafe class NativeAnimationClockCodec
{
    public static AnimationClockState Capture(NativeCharacter* c)
        => new(c->Timeline.OverallSpeed, c->Timeline.TimelineSequencer.TimelineSpeeds.ToArray());

    public static void Write(NativeCharacter* c, AnimationClockState state, Action<uint, float> setSlotSpeed)
    {
        state.Validate();
        c->Timeline.OverallSpeed = state.OverallSpeed;
        for (uint slot = 0; slot < 14; slot++)
        {
            var speed = state.SlotSpeeds[slot];
            // SetSlotSpeed marks the scheduler dirty only when its cached value
            // changes. Never pre-fill the cache; even a same-value repair must
            // trigger the native dirty path before restoring the requested value.
            if (c->Timeline.TimelineSequencer.TimelineSpeeds[(int)slot] == speed)
                setSlotSpeed(slot, speed == 0 ? 1 : 0);
            setSlotSpeed(slot, speed);
        }
    }
}

using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

public static unsafe class NativeAnimationClockCodec
{
    public static AnimationClockState Capture(NativeCharacter* c)
        => new(c->Timeline.OverallSpeed, c->Timeline.TimelineSequencer.TimelineSpeeds.ToArray());

    public static void Write(NativeCharacter* c, AnimationClockState state)
    {
        state.Validate();
        c->Timeline.OverallSpeed = state.OverallSpeed;
        state.SlotSpeeds.CopyTo(c->Timeline.TimelineSequencer.TimelineSpeeds);
    }
}

using System.Numerics;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

// The native nameplate caches an already-scaled attachment offset. Changing the
// draw transform alone leaves this cache at its old height. Owned previews only.
public static unsafe class NativeLocalNameplateCodec
{
    public static bool Write(NativeCharacter* c, Vector3 attachment, float height)
    {
        if (c->GameObject.DrawObject == null) return false;
        Vector3 scale = c->GameObject.DrawObject->Object.Scale;
        Vector3 offset = c->GameObject.DrawOffset;
        var target = attachment * scale + offset + new Vector3(0, height, 0);
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z)
            || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || !float.IsFinite(scale.Z)
            || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0) return false;
        Vector3 current = c->GameObject.NameplateOffset;
        Vector3 pending = c->GameObject.NameplateOffsetTarget;
        if (current == target && pending == target) return false;
        // Update both ends so the old interpolated position cannot keep the name
        // embedded in a newly enlarged model. The native extra text gap remains.
        c->GameObject.NameplateOffset = target;
        c->GameObject.NameplateOffsetTarget = target;
        return true;
    }
}

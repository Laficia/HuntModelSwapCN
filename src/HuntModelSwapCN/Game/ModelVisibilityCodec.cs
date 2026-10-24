using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace HuntModelSwapCN.Game;

public static unsafe class ModelVisibilityCodec
{
    public static bool IsHidden(GameObject* actor) => (actor->RenderFlags & VisibilityFlags.Model) != 0;
    public static void SetHidden(GameObject* actor, bool hidden)
        => actor->RenderFlags = hidden ? actor->RenderFlags | VisibilityFlags.Model : actor->RenderFlags & ~VisibilityFlags.Model;
}

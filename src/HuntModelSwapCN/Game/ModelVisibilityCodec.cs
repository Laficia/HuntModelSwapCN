using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace HuntModelSwapCN.Game;

public static unsafe class ModelVisibilityCodec
{
    public static string? CannotHideSelected(GameObject* actor)
    {
        if (actor == null || actor->ObjectKind != ObjectKind.BattleNpc || actor->SubKind != 5)
            return "请先选中一只战斗小怪（主动或被动均可）。";
        if (actor->OwnerId is not (0 or 0xe0000000)) return "不能隐藏宠物或其他角色所属对象。";
        // Passive enemies can have IsHostile unset. Selection is explicit, so
        // aggressiveness is not a condition for this reversible visual action.
        return null;
    }

    public static bool IsHidden(GameObject* actor) => (actor->RenderFlags & VisibilityFlags.Model) != 0;
    public static void SetHidden(GameObject* actor, bool hidden)
        => actor->RenderFlags = hidden ? actor->RenderFlags | VisibilityFlags.Model : actor->RenderFlags & ~VisibilityFlags.Model;
}

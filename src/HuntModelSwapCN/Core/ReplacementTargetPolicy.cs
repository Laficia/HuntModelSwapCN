namespace HuntModelSwapCN.Core;

public readonly record struct ReplacementTargetFacts(bool IsBattleNpc, byte SubKind, uint NameId,
    uint OwnerId, uint CurrentHp, bool IsTransformed, bool HasDrawObject);

public static class ReplacementTargetPolicy
{
    public static string? CannotApply(ReplacementTargetFacts target)
    {
        if (!target.IsBattleNpc) return "请选中战斗小怪或可攻击木人，不支持装饰家具。";
        var dummy = StrikingDummyPolicy.IsDummy(target.NameId);
        // Some actual dummy targets use a different BNpc subkind/owner relationship.
        // Their original BNpcName identity is the exception, never their display name.
        if (!dummy && target.SubKind != 5) return "当前目标不是可替换的战斗小怪或可攻击木人。";
        if (!dummy && target.OwnerId is not (0 or 0xe0000000)) return "不能替换宠物或其他角色所属对象。";
        if (target.CurrentHp == 0) return "请先选择存活的战斗小怪或木人。";
        if (target.IsTransformed) return "当前目标正在使用游戏变身，请稍后再试。";
        // Initial appearance capture/write is null-safe and EnableDraw creates the
        // replacement model. Animation controls separately require a loaded model.
        if (!dummy && !target.HasDrawObject) return "当前目标的模型仍在加载。";
        return null;
    }
}

using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public static class HuntAppearanceReplacement
{
    public static VisualState Create(VisualState current, HuntAppearance appearance, float scale)
        => ModelSize.Apply(current with
        {
            ModelId = appearance.ModelId,
            SecondaryModelId = -1,
            Customize = appearance.Customize.ToArray(),
            Equipment = appearance.Equipment.ToArray(),
            Weapons = [appearance.MainHand, appearance.OffHand, 0],
            Flags1 = (byte)(current.Flags1 & ~1),
            Flags2 = (byte)((current.Flags2 & ~0x11) | (appearance.Visor ? 0x10 : 0)),
            NameBytes = DisplayName.Encode(appearance.Name),
            NameplateIcon = appearance.NameplateIcon,
            Level = appearance.Level > 0 ? appearance.Level
                : throw new InvalidOperationException("尚未配置此版本的狩猎怪等级。"),
        }, scale);
}

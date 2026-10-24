using HuntModelSwapCN.Core;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

// No game functions here. Kept separate so memory round trips can be verified offline.
public static unsafe class NativeAppearanceCodec
{
    public static VisualState Capture(NativeCharacter* c)
        => new(c->ModelContainer.ModelCharaId, c->ModelContainer.ModelCharaId_2, c->ModelScale,
            c->DrawData.CustomizeData.Data.ToArray(),
            c->DrawData.EquipmentModelIds.ToArray().Select(e => e.Value).ToArray(),
            c->DrawData.WeaponData.ToArray().Select(w => w.ModelId.Value).ToArray(),
            c->DrawData.Flags1, c->DrawData.Flags2, c->GameObject.Name.ToArray(), c->Level,
            c->GameObject.Scale, c->GameObject.DrawObject != null
                ? c->GameObject.DrawObject->Object.Scale : new System.Numerics.Vector3(c->GameObject.Scale), c->Icon);

    public static void Validate(VisualState state)
    {
        if (state.Customize.Length != 26 || state.Equipment.Length != 10 || state.Weapons.Length != 3
            || state.ModelId < 0 || !float.IsFinite(state.ModelScale) || state.ModelScale <= 0
            || state.NameBytes.Length != DisplayName.Capacity || !state.NameBytes.Contains((byte)0)
            || !float.IsFinite(state.ObjectScale) || state.ObjectScale <= 0
            || !float.IsFinite(state.DrawScale.X) || !float.IsFinite(state.DrawScale.Y) || !float.IsFinite(state.DrawScale.Z)
            || state.DrawScale.X <= 0 || state.DrawScale.Y <= 0 || state.DrawScale.Z <= 0)
            throw new InvalidOperationException("外观数据无效，未应用替换。");
    }

    public static void Write(NativeCharacter* c, VisualState state)
    {
        Validate(state);
        c->ModelContainer.ModelCharaId = state.ModelId;
        c->ModelContainer.ModelCharaId_2 = state.SecondaryModelId;
        WriteScale(c, state);
        state.Customize.CopyTo(c->DrawData.CustomizeData.Data);
        for (var i = 0; i < 10; i++) c->DrawData.EquipmentModelIds[i].Value = state.Equipment[i];
        for (var i = 0; i < 3; i++) c->DrawData.WeaponData[i].ModelId.Value = state.Weapons[i];
        c->DrawData.Flags1 = state.Flags1;
        c->DrawData.Flags2 = state.Flags2;
        WritePresentation(c, state);
    }

    // Both object and draw transforms can otherwise retain the source creature's size.
    public static void WriteScale(NativeCharacter* c, VisualState state)
    {
        c->ModelScale = state.ModelScale;
        c->GameObject.Scale = state.ObjectScale;
        if (c->GameObject.DrawObject != null && (System.Numerics.Vector3)c->GameObject.DrawObject->Object.Scale != state.DrawScale)
        {
            c->GameObject.DrawObject->Object.Scale = state.DrawScale;
            c->GameObject.DrawObject->Object.IsTransformChanged = true;
        }
    }

    // NameId is deliberately untouched: it participates in actor identity and hunt recognition.
    // Only request a UI refresh when the display fields actually differ.
    public static bool WritePresentation(NativeCharacter* c, VisualState state)
    {
        if (state.NameBytes.Length != DisplayName.Capacity || !state.NameBytes.Contains((byte)0))
            throw new InvalidOperationException("名称缓冲区无效。");
        var changed = false;
        if (!c->GameObject.Name.SequenceEqual(state.NameBytes))
        {
            state.NameBytes.CopyTo(c->GameObject.Name);
            c->GameObject.UpdateFlags |= ObjectUpdateFlags.Name;
            changed = true;
        }
        if (c->Level != state.Level)
        {
            c->Level = state.Level;
            changed = true;
        }
        if (c->Icon != state.NameplateIcon)
        {
            c->Icon = state.NameplateIcon;
            changed = true;
        }
        return changed;
    }
}

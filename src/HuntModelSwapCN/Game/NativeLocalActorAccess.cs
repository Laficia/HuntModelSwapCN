using System.Security.Cryptography;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using HuntModelSwapCN.Catalog;
using HuntModelSwapCN.Core;
using NativeCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace HuntModelSwapCN.Game;

public sealed unsafe class NativeLocalActorAccess(IObjectTable objects, ITargetManager targets,
    NativeActorAccess actors, INamePlateGui nameplates) : ILocalActorAccess
{
    public LocalSpawn Create(HuntAppearance appearance, float scale, float distance)
    {
        ModelSize.Validate(scale);
        if (objects.LocalPlayer is not { } player || !player.IsValid())
            throw new InvalidOperationException("请先进入游戏场景。");
        var position = SpawnPlacement.InFront(player.Position, player.Rotation, distance);
        var name = DisplayName.Encode(appearance.Name);
        var token = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))) | 1UL;
        name = LocalPreviewName.Stamp(name, token); // Validate before allocating a native actor.
        var manager = ClientObjectManager.Instance();
        if (manager == null) throw new InvalidOperationException("本地对象管理器尚未就绪。");
        var index = manager->CreateBattleCharacter();
        if (index == uint.MaxValue) throw new InvalidOperationException("没有可用的本地预览对象槽位。");
        if (index >= manager->BattleCharas.Length)
            throw new InvalidOperationException("创建接口返回了无效的本地槽位，已停止操作。");
        var slot = (ushort)index;
        var c = manager->GetObjectByIndex(slot);
        try
        {
            if (c == null || c->GameObject.ObjectIndex is < 200 or > 448)
                throw new InvalidOperationException("创建的对象不属于本地预览范围。");
            c->GameObject.DisableDraw();
            c->CharacterSetup.SetupBNpc(appearance.BaseId, appearance.NameId);
            c->GameObject.DisableDraw();
            c->GameObject.ObjectKind = ObjectKind.BattleNpc;
            c->GameObject.SubKind = 5; // Combatant in both CN Dalamud and ClientStructs.
            // Preview actors have no real hunt identity; HHT must not record them as live S/A/B marks.
            c->GameObject.BaseId = 0;
            c->NameId = 0;
            c->GameObject.OwnerId = 0xe0000000;
            c->Health = c->MaxHealth = 1;
            NativeLocalPresentationCodec.Write(c, appearance.Battalion, appearance.IsAggressive);
            c->GameObject.SetPosition(position.X, position.Y, position.Z);
            c->GameObject.SetRotation(player.Rotation + MathF.PI);
            c->GameObject.TargetableStatus |= ObjectTargetableFlags.IsTargetable;
            c->GameObject.SetReadyToDraw();
            var state = NativeActorAccess.Replacement(NativeAppearanceCodec.Capture(c), appearance, scale)
                with { NameBytes = name };
            NativeAppearanceCodec.Write(c, state);
            if (objects.CreateObjectReference((nint)c) is not IBattleNpc npc)
                throw new InvalidOperationException("无法解析新生成的预览怪物。");
            var lease = new LocalActorLease(slot, (nint)manager, NativeActorAccess.Identity(npc), token, appearance.Battalion, appearance.IsAggressive);
            return new(lease, state);
        }
        catch
        {
            if (c != null && manager->GetObjectByIndex(slot) == c) manager->DeleteObjectByIndex(slot, 0);
            throw;
        }
    }

    public bool Exists(LocalActorLease lease)
    {
        var manager = ClientObjectManager.Instance();
        if (manager == null || (nint)manager != lease.Manager || lease.ClientIndex >= manager->BattleCharas.Length)
            return false;
        var c = manager->GetObjectByIndex(lease.ClientIndex);
        return c != null && (nint)c == lease.Actor.Address && c->GameObject.ObjectIndex is >= 200 and <= 448
            && actors.Exists(lease.Actor) && LocalPreviewName.Matches(c->GameObject.Name, lease.Token);
    }

    public void RefreshPresentation(LocalActorLease lease)
    {
        if (!Exists(lease)) return;
        var c = ClientObjectManager.Instance()->GetObjectByIndex(lease.ClientIndex);
        if (NativeLocalPresentationCodec.Write(c, lease.Battalion, lease.IsAggressive)) nameplates.RequestRedraw();
        var draw = c->GameObject.DrawObject;
        if (draw == null || draw->LoadState != 3) return;
        // Native nameplate attachment 45; the game falls back to attachment 46.
        // Read the unscaled model point anew, never multiply the previous cache.
        var bone = draw->HasAttachBone(45) ? 45 : draw->HasAttachBone(46) ? 46 : -1;
        FFXIVClientStructs.FFXIV.Common.Math.Vector3 attachment = default;
        if (bone >= 0) draw->GetAttachBoneLocalLocation(&attachment, bone);
        NativeLocalNameplateCodec.Write(c, attachment, c->GameObject.GetHeight());
    }

    public void Delete(LocalActorLease lease)
    {
        if (!Exists(lease)) return;
        if (targets.Target?.Address == lease.Actor.Address) targets.Target = null;
        ClientObjectManager.Instance()->DeleteObjectByIndex(lease.ClientIndex, 0);
        nameplates.RequestRedraw();
    }
}

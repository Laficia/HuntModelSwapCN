using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public sealed record LocalActorLease(ushort ClientIndex, nint Manager, ActorIdentity Actor, ulong Token, byte Battalion = 0,
    bool IsAggressive = true);
public sealed record LocalSpawn(LocalActorLease Lease, VisualState State);
public readonly record struct LocalPlacement(Vector3 Position, float Rotation, bool Targetable = true)
{
    public void Validate()
    {
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z) || !float.IsFinite(Rotation))
            throw new InvalidOperationException("本地模型位置或朝向无效。");
    }
}
public interface ILocalActorAccess
{
    LocalSpawn Create(HuntAppearance appearance, float scale, float distance);
    LocalSpawn CreateAt(HuntAppearance appearance, float scale, LocalPlacement placement)
        => throw new InvalidOperationException("当前接口不支持原位生成。");
    bool Exists(LocalActorLease lease);
    void RefreshPresentation(LocalActorLease lease);
    void Delete(LocalActorLease lease);
}

public static class SpawnPlacement
{
    public static Vector3 InFront(Vector3 position, float rotation, float distance)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)
            || !float.IsFinite(rotation) || !float.IsFinite(distance) || distance is < 2 or > 30)
            throw new InvalidOperationException("生成位置无效，距离应在 2～30 米之间。");
        return position + new Vector3(MathF.Sin(rotation), 0, MathF.Cos(rotation)) * distance;
    }
}

public static class LocalPreviewName
{
    // Keep an ownership nonce after the C-string terminator. This never changes the visible name.
    private const int TokenOffset = DisplayName.Capacity - sizeof(ulong);
    public static byte[] Stamp(byte[] name, ulong token)
    {
        if (name.Length != DisplayName.Capacity || token == 0 || Array.IndexOf(name, (byte)0) is < 0 or >= TokenOffset)
            throw new InvalidOperationException("预览名称无效或过长。");
        var copy = name.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(TokenOffset), token);
        return copy;
    }
    public static bool Matches(ReadOnlySpan<byte> name, ulong token) => name.Length == DisplayName.Capacity
        && token != 0 && name[..TokenOffset].Contains((byte)0)
        && BinaryPrimitives.ReadUInt64LittleEndian(name[TokenOffset..]) == token;
}

// One owned client-only actor. Never delete an arbitrary object-table slot or a networked actor.
public sealed class LocalPreview(SwapSession session, ILocalActorAccess access, Func<SwapEntry, bool?>? aggressionFor = null)
{
    public LocalActorLease? Actor { get; private set; }
    public bool Owns(ActorIdentity actor) => Actor?.Actor == actor;
    public bool OwnsLive(ActorIdentity actor) => Actor is { } lease && lease.Actor == actor && access.Exists(lease);

    // Nameplate visibility bit 0x2 draws the HP gauge; keep text and all other bits intact.
    // Revalidate the native ownership marker even if a slot has the same object identity.
    public int NameplateVisibility(ActorIdentity actor, int flags)
        => OwnsLive(actor) ? flags & ~0x2 : flags;

    public ActorIdentity Spawn(HuntAppearance appearance, float scale, float distance)
        => SpawnCore(appearance, scale, () => access.Create(appearance, scale, distance));

    public ActorIdentity SpawnAt(HuntAppearance appearance, float scale, LocalPlacement placement)
    {
        placement.Validate();
        return SpawnCore(appearance, scale, () => access.CreateAt(appearance, scale, placement));
    }

    private ActorIdentity SpawnCore(HuntAppearance appearance, float scale, Func<LocalSpawn> create)
    {
        ModelSize.Validate(scale);
        Clear();
        var created = create();
        Actor = created.Lease;
        try
        {
            session.Apply(created.Lease.Actor, "本地预览", appearance.Name, created.State);
            return created.Lease.Actor;
        }
        catch { Clear(); throw; }
    }

    public VisualState PreserveOwnership(ActorIdentity actor, VisualState state)
        => Owns(actor) ? state with { NameBytes = LocalPreviewName.Stamp(state.NameBytes, Actor!.Token) } : state;

    public void Tick()
    {
        if (Actor is not { } lease) return;
        if (access.Exists(lease))
        {
            // Replacing an already spawned B with A/S (or the reverse) must update the icon too.
            var entry = session.Entries.FirstOrDefault(e => e.Actor == lease.Actor);
            if (entry is not null && !entry.Restoring && aggressionFor?.Invoke(entry) is { } aggressive
                && aggressive != lease.IsAggressive)
                Actor = lease = lease with { IsAggressive = aggressive };
            access.RefreshPresentation(lease);
            return;
        }
        session.Forget(lease.Actor);
        Actor = null;
    }

    public void Clear()
    {
        if (Actor is not { } lease) return;
        if (access.Exists(lease)) access.Delete(lease);
        session.Forget(lease.Actor);
        Actor = null;
    }

    // Territory/logout has already discarded old actors; never delete using an old scene's index.
    public void ForgetScene()
    {
        if (Actor is { } lease) session.Forget(lease.Actor);
        Actor = null;
    }
}

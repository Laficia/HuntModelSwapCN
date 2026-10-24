using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Core;

public sealed record LocalActorLease(ushort ClientIndex, nint Manager, ActorIdentity Actor, ulong Token);
public sealed record LocalSpawn(LocalActorLease Lease, VisualState State);
public interface ILocalActorAccess
{
    LocalSpawn Create(HuntAppearance appearance, float scale, float distance);
    bool Exists(LocalActorLease lease);
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
public sealed class LocalPreview(SwapSession session, ILocalActorAccess access)
{
    public LocalActorLease? Actor { get; private set; }
    public bool Owns(ActorIdentity actor) => Actor?.Actor == actor;

    // Nameplate visibility bit 0x2 draws the HP gauge; keep text and all other bits intact.
    // Revalidate the native ownership marker even if a slot has the same object identity.
    public int NameplateVisibility(ActorIdentity actor, int flags)
        => Actor is { } lease && lease.Actor == actor && access.Exists(lease) ? flags & ~0x2 : flags;

    public ActorIdentity Spawn(HuntAppearance appearance, float scale, float distance)
    {
        ModelSize.Validate(scale);
        Clear();
        var created = access.Create(appearance, scale, distance);
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
        if (Actor is not { } lease || access.Exists(lease)) return;
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

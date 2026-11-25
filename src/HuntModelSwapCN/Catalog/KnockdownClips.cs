using System.Buffers.Binary;

namespace HuntModelSwapCN.Catalog;

public static class KnockdownClips
{
    public static string RisePath(string family)
    {
        var folder = family.Length == 0 ? "human/c0101" : $"{(family[0] == 'm' ? "monster" : "demihuman")}/{family}";
        return $"chara/{folder}/animation/a0001/bt_common/normal/revive.pap";
    }

    // Some PAPs exist as empty placeholders. Match a full name in the animation table.
    public static bool Contains(ReadOnlySpan<byte> pap, ReadOnlySpan<byte> clip)
    {
        if (pap.Length < 26 || !pap.StartsWith("pap "u8) || clip.IsEmpty) return false;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(pap[8..]);
        var offset = BinaryPrimitives.ReadInt32LittleEndian(pap[14..]);
        if (offset < 26 || offset > pap.Length || count > (pap.Length - offset) / 40) return false;
        for (var i = 0; i < count; i++)
        {
            var name = pap.Slice(offset + i * 40, 32);
            var end = name.IndexOf((byte)0);
            if ((end < 0 ? name : name[..end]).SequenceEqual(clip)) return true;
        }
        return false;
    }
}

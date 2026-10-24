using System.Text;

namespace HuntModelSwapCN.Core;

public static class DisplayName
{
    public const int Capacity = 64;
    public static byte[] Encode(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0')
            || Encoding.UTF8.GetByteCount(name) >= Capacity)
            throw new InvalidOperationException("怪物名称为空或超过客户端名称长度，未应用替换。");
        var buffer = new byte[Capacity];
        Encoding.UTF8.GetBytes(name, buffer);
        return buffer;
    }
}

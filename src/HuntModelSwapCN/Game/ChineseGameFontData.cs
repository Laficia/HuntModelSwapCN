using Dalamud.Interface.GameFonts;

namespace HuntModelSwapCN.Game;

public sealed record GameFontTexture(int Width, int Height, byte[] Pixels);
public sealed record GameFontGlyph(char Character, int Width, int Height, int Advance, int OffsetY, byte[] Alpha);
public sealed record GameFontKerning(char Left, char Right, int Offset);

// Read only the user's installed game resources. No game font files are distributed.
public sealed record ChineseGameFontData(int LineHeight, int Ascent, IReadOnlyList<GameFontGlyph> Glyphs, IReadOnlyList<GameFontKerning> Kerning)
{
    public const string FontPath = "common/font/chnaxis_140.fdt";
    public static string TexturePath(int fileIndex) => $"common/font/font_chn_{fileIndex + 1}.tex";

    public static ChineseGameFontData Load(Func<string, byte[]?> readFont, Func<string, GameFontTexture?> readTexture)
    {
        var fdt = new FdtReader(readFont(FontPath) ?? throw new InvalidOperationException("当前客户端缺少国服 ChnAXIS 字体。"));
        if (fdt.Glyphs.Count is < 1 or > 65536 || fdt.FontHeader.LineHeight is < 1 or > 128)
            throw new InvalidOperationException("国服游戏字体的字形表无效。");
        Dictionary<int, GameFontTexture> textures = [];
        HashSet<char> seen = [];
        List<GameFontGlyph> glyphs = [];
        foreach (var glyph in fdt.Glyphs)
        {
            if (glyph.CharInt is < 32 or >= 65535 || char.IsSurrogate((char)glyph.CharInt)) continue;
            if (!seen.Add((char)glyph.CharInt)) continue; // CN FDT includes two identical space entries.
            if (!textures.TryGetValue(glyph.TextureFileIndex, out var texture))
            {
                texture = readTexture(TexturePath(glyph.TextureFileIndex))
                    ?? throw new InvalidOperationException("当前客户端缺少国服字体贴图。");
                if (texture.Width is < 1 or > 8192 || texture.Height is < 1 or > 8192
                    || texture.Pixels.Length != checked(texture.Width * texture.Height * 4))
                    throw new InvalidOperationException("国服字体贴图尺寸无效。");
                textures.Add(glyph.TextureFileIndex, texture);
            }
            var w = (int)glyph.BoundingWidth; var h = (int)glyph.BoundingHeight;
            if (glyph.TextureOffsetX + w > texture.Width || glyph.TextureOffsetY + h > texture.Height)
                throw new InvalidOperationException("国服字体字形超出了贴图范围。");
            var alpha = new byte[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    alpha[y * w + x] = texture.Pixels[((glyph.TextureOffsetY + y) * texture.Width + glyph.TextureOffsetX + x) * 4
                        + glyph.TextureChannelByteIndex];
            glyphs.Add(new((char)glyph.CharInt, w, h, glyph.AdvanceWidth, glyph.CurrentOffsetY, alpha));
        }
        var kerning = fdt.Distances.Where(k => k.LeftInt is >= 32 and < 65535 && k.RightInt is >= 32 and < 65535)
            .Select(k => new GameFontKerning((char)k.LeftInt, (char)k.RightInt, k.RightOffset)).ToArray();
        return new(fdt.FontHeader.LineHeight, fdt.FontHeader.Ascent, glyphs, kerning);
    }
}

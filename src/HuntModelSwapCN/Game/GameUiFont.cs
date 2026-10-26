using System.Numerics;
using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin.Services;
using Lumina.Data.Files;

namespace HuntModelSwapCN.Game;

// The generic SDK AXIS handle can point at the Japanese FDT even on CN. Build
// this plugin's font from ChnAXIS directly, without changing shared SDK state.
public sealed class GameUiFont(IFontAtlas atlas, IDataManager data, IPluginLog log) : IDisposable
{
    private Task<ChineseGameFontData>? loading;
    private IFontHandle? handle;
    private bool reported;
    private bool disposed;

    public IDisposable? Push()
    {
        if (disposed) return null;
        loading ??= Task.Run(() => ChineseGameFontData.Load(
            path => data.GetFile(path)?.Data,
            path => data.GetFile<TexFile>(path) is { } tex ? new(tex.Header.Width, tex.Header.Height, tex.ImageData) : null));
        if (!loading.IsCompleted) return null;
        if (loading.IsCompletedSuccessfully)
            handle ??= Create(loading.Result);
        var error = loading.Exception ?? handle?.LoadException;
        if (error is not null && !reported)
        {
            reported = true;
            log.Error(error, "读取国服原版窗口字体失败，暂时沿用卫月字体。");
        }
        return handle?.Push();
    }

    internal sealed record BuildPlan(float Scale, List<(int Rect, GameFontGlyph Glyph, int Width, int Height)> Rectangles);

    private IFontHandle Create(ChineseGameFontData fontData)
    {
        var plans = new System.Collections.Concurrent.ConcurrentDictionary<nint, BuildPlan>();
        return atlas.NewDelegateFontHandle(build => {
            build.OnPreBuild(pre => {
                var scale = pre.Scale;
                if (!float.IsFinite(scale) || scale is < 0.25f or > 8)
                    throw new InvalidOperationException("窗口字体缩放无效。");
                pre.Font = pre.AddDalamudAssetFont(DalamudAsset.NotoSansCjkMedium,
                    new() { GlyphRanges = [' ', ' ', '\0'], SizePx = fontData.LineHeight * scale });
                pre.SetFontScaleMode(pre.Font, FontScaleMode.SkipHandling);
                var key = AtlasKey(pre.NewImAtlas);
                var plan = Register(pre.NewImAtlas, pre.Font, fontData, scale);
                plans[key] = plan;
                pre.DisposeWithAtlas(() => ((ICollection<KeyValuePair<nint, BuildPlan>>)plans).Remove(new(key, plan)));
            });
            build.OnPostBuild(post => {
                if (!plans.TryRemove(AtlasKey(post.NewImAtlas), out var plan))
                    throw new InvalidOperationException("字体构建状态已失效。");
                Populate(post.NewImAtlas, post.Font, fontData, plan);
            });
        });
    }

    private static unsafe nint AtlasKey(ImFontAtlasPtr imAtlas) => (nint)imAtlas.Handle;

    internal static BuildPlan Register(ImFontAtlasPtr imAtlas, ImFontPtr font, ChineseGameFontData fontData, float scale)
    {
        imAtlas.TexDesiredWidth = Math.Max(imAtlas.TexDesiredWidth, 2048);
        imAtlas.TexDesiredHeight = Math.Max(imAtlas.TexDesiredHeight, 2048);
        List<(int Rect, GameFontGlyph Glyph, int Width, int Height)> rectangles = [];
        foreach (var glyph in fontData.Glyphs)
        {
            var w = Math.Max(1, (int)MathF.Ceiling(glyph.Width * scale));
            var h = Math.Max(1, (int)MathF.Ceiling(glyph.Height * scale));
            var id = imAtlas.AddCustomRectFontGlyph(font, glyph.Character, w, h,
                glyph.Advance * scale, new Vector2(0, glyph.OffsetY * scale));
            rectangles.Add((id, glyph, w, h));
        }
        return new(scale, rectangles);
    }

    internal static unsafe void Populate(ImFontAtlasPtr imAtlas, ImFontPtr font, ChineseGameFontData fontData, BuildPlan plan)
    {
        var buffers = new (nint Pointer, int Width, int Height)[imAtlas.Textures.Size];
        for (var i = 0; i < buffers.Length; i++)
        {
            byte* pixels = null; var w = 0; var h = 0;
            imAtlas.GetTexDataAsAlpha8(i, ref pixels, ref w, ref h);
            if (pixels == null || w < 1 || h < 1) throw new InvalidOperationException("字体图集尚未就绪。");
            buffers[i] = ((nint)pixels, w, h);
        }
        foreach (var (id, glyph, w, h) in plan.Rectangles)
        {
            var rect = new ImFontAtlasCustomRectPtr(imAtlas.GetCustomRectByIndex(id));
            if (rect.IsNull || rect.TextureIndex >= buffers.Length) throw new InvalidOperationException("字体图集索引无效。");
            var buffer = buffers[rect.TextureIndex];
            if (rect.X + w > buffer.Width || rect.Y + h > buffer.Height)
                throw new InvalidOperationException("字体字形超出图集范围。");
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var sx = Math.Min(glyph.Width - 1, (int)(x / plan.Scale));
                    var sy = Math.Min(glyph.Height - 1, (int)(y / plan.Scale));
                    ((byte*)buffer.Pointer)[(rect.Y + y) * buffer.Width + rect.X + x] = sx < 0 || sy < 0 ? (byte)0 : glyph.Alpha[sy * glyph.Width + sx];
                }
        }
        font.FontSize = fontData.LineHeight * plan.Scale;
        font.Ascent = fontData.Ascent * plan.Scale;
        font.Descent = (fontData.Ascent - fontData.LineHeight) * plan.Scale;
        font.FallbackChar = '?';
        font.EllipsisChar = '…';
        foreach (var pair in fontData.Kerning) font.AddKerningPair(pair.Left, pair.Right, pair.Offset * plan.Scale);
        font.BuildLookupTable();
    }

    public void Dispose() { disposed = true; handle?.Dispose(); handle = null; }
}

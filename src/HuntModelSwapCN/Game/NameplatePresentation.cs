using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin.Services;
using HuntModelSwapCN.Core;
using HuntModelSwapCN.Catalog;

namespace HuntModelSwapCN.Game;

// Battle-NPC nameplates may resolve BNpcName again instead of using GameObject.Name.
// Change the rendered name through Dalamud's scoped service; keep the real NameId intact.
public sealed class NameplatePresentation : IDisposable
{
    private readonly INamePlateGui nameplates;
    private readonly SwapSession session;
    private readonly LocalPreview localPreview;
    private readonly IPluginLog log;
    private readonly IReadOnlyList<HuntAppearance> catalog;
    private readonly Func<ActorIdentity, bool>? hiddenSource;
    private bool disposed;
    public NameplatePresentation(INamePlateGui nameplates, SwapSession session, LocalPreview localPreview, IPluginLog log,
        IReadOnlyList<HuntAppearance> catalog, Func<ActorIdentity, bool>? hiddenSource = null)
    {
        this.nameplates = nameplates;
        this.session = session;
        this.localPreview = localPreview;
        this.log = log;
        this.catalog = catalog;
        this.hiddenSource = hiddenSource;
        nameplates.OnDataUpdate += UpdateLocalVisibility;
        nameplates.OnNamePlateUpdate += Update;
    }

    private void UpdateLocalVisibility(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (disposed || localPreview.Actor is not { } lease) return;
        try
        {
            foreach (var handler in handlers)
            {
                if (hiddenSource is not null && handler.GameObject is IBattleNpc source && source.IsValid()
                    && hiddenSource(NativeActorAccess.Identity(source)))
                {
                    handler.VisibilityFlags = 0;
                    continue;
                }
                // Only resolve the owned preview, not every nameplate in the scene each frame.
                if (handler.GameObjectId != lease.Actor.ObjectId
                    || handler.GameObject is not IBattleNpc npc || !npc.IsValid()) continue;
                var flags = handler.VisibilityFlags;
                var visible = localPreview.NameplateVisibility(NativeActorAccess.Identity(npc), flags);
                if (visible != flags) handler.VisibilityFlags = visible;
            }
        }
        catch (Exception ex) { log.Error(ex, "隐藏本地预览头顶血条失败"); }
    }

    private void Update(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (disposed || session.Entries.Count == 0) return;
        try
        {
            foreach (var handler in handlers)
            {
                if (handler.GameObject is not IBattleNpc npc || !npc.IsValid()) continue;
                var identity = NativeActorAccess.Identity(npc);
                if (localPreview.Owns(identity) && !localPreview.OwnsLive(identity)) continue;
                if (session.NameFor(identity) is { } name) handler.SetField(NamePlateStringField.Name, name);
                var entry = session.Entries.FirstOrDefault(e => e.Actor == identity);
                if (NameplateLevelPolicy.Resolve(entry) is { } level)
                    handler.SetField(NamePlateStringField.LevelPrefix, level);
                if (NameplateIconPolicy.Resolve(entry, catalog) is { } icon) handler.NameIconId = icon;
                // Restore/redraw uses the game's freshly calculated original icon. No cached UI slots.
            }
        }
        catch (Exception ex) { log.Error(ex, "更新替换对象头顶名称失败"); }
    }

    public void Dispose()
    {
        disposed = true;
        nameplates.OnDataUpdate -= UpdateLocalVisibility;
        nameplates.OnNamePlateUpdate -= Update;
        nameplates.RequestRedraw();
    }
}

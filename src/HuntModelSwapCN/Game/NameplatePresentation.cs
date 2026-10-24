using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin.Services;
using HuntModelSwapCN.Core;

namespace HuntModelSwapCN.Game;

// Battle-NPC nameplates may resolve BNpcName again instead of using GameObject.Name.
// Change the rendered name through Dalamud's scoped service; keep the real NameId intact.
public sealed class NameplatePresentation : IDisposable
{
    private readonly INamePlateGui nameplates;
    private readonly SwapSession session;
    private readonly IPluginLog log;
    private bool disposed;
    public NameplatePresentation(INamePlateGui nameplates, SwapSession session, IPluginLog log)
    {
        this.nameplates = nameplates;
        this.session = session;
        this.log = log;
        nameplates.OnNamePlateUpdate += Update;
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
                if (session.NameFor(identity) is { } name) handler.SetField(NamePlateStringField.Name, name);
            }
        }
        catch (Exception ex) { log.Error(ex, "更新替换对象头顶名称失败"); }
    }

    public void Dispose()
    {
        disposed = true;
        nameplates.OnNamePlateUpdate -= Update;
        nameplates.RequestRedraw();
    }
}

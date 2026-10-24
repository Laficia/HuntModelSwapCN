using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using HuntModelSwapCN.Catalog;
using HuntModelSwapCN.Core;
using HuntModelSwapCN.Game;
using Lumina.Excel.Sheets;

namespace HuntModelSwapCN;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IFramework framework;
    private readonly ICommandManager commands;
    private readonly IClientState client;
    private readonly ICondition condition;
    private readonly ITargetManager targets;
    private readonly IPluginLog log;
    private readonly NativeActorAccess actors;
    private readonly SwapSession session;
    private readonly IReadOnlyList<HuntAppearance> catalog;
    private readonly ConcurrentQueue<Request> requests = new();
    private volatile UiSnapshot snapshot = new(null, "未选中小怪", "请先进入游戏。", [], "就绪");
    private bool visible;
    private volatile bool disposed;
    private string search = "";
    private int rankFilter = 3;
    private int expansionFilter = -1;
    private int selected = -1;
    private float scale = 1;
    private string status = "就绪";
    private long epoch;
    private record Request(long Epoch, string Action, ActorIdentity? Actor, int Selection = -1, float Scale = 1);
    private sealed record UiSnapshot(ActorIdentity? Target, string TargetName, string? CannotApply,
        SwapEntry[] Entries, string Status);

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, ICommandManager commands,
        IClientState client, ICondition condition, IObjectTable objects, IDataManager data,
        ITargetManager targets, IPluginLog log, INamePlateGui nameplates)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        this.commands = commands;
        this.client = client;
        this.condition = condition;
        this.targets = targets;
        this.log = log;
        actors = new(objects, nameplates);
        session = new(actors);
        catalog = HuntCatalog.Load(data.GetExcelSheet<TerritoryType>(), data.GetExcelSheet<BNpcName>(),
            data.GetExcelSheet<PlaceName>());
        commands.AddHandler("/hms", new CommandInfo(OnCommand) { HelpMessage = "打开狩猎模型替换；/hms restore 恢复全部。" });
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenMainUi += Open;
        pluginInterface.UiBuilder.OpenConfigUi += Open;
        client.TerritoryChanged += OnTerritoryChanged;
        client.Logout += OnLogout;
        framework.Update += Update;
    }

    private void Open() => visible = true;
    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("restore", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "all", null));
        else visible = !visible;
    }

    private void OnTerritoryChanged(uint territory) => ClearSession();
    private void OnLogout(int type, int code) => ClearSession();
    private void ClearSession()
    {
        Interlocked.Increment(ref epoch);
        requests.Clear();
        session.ForgetDespawned();
        snapshot = new(null, "未选中小怪", "正在切换场景。", [], "已结束上一场景的临时替换。");
    }

    private bool Loading => !client.IsLoggedIn || condition[ConditionFlag.BetweenAreas]
        || condition[ConditionFlag.BetweenAreas51];

    private void Update(IFramework _)
    {
        if (disposed || Loading) return;
        try
        {
            // Complete last frame's redraws before processing new UI requests.
            session.Tick();
            while (requests.TryDequeue(out var request))
            {
                if (request.Epoch != Interlocked.Read(ref epoch)) continue;
                try { Execute(request); }
                catch (Exception ex) { status = ex.Message; log.Error(ex, "模型替换操作失败"); }
            }
            var npc = targets.Target as IBattleNpc;
            ActorIdentity? identity = npc is { } && npc.IsValid() ? NativeActorAccess.Identity(npc) : null;
            snapshot = new(identity, npc?.Name.ToString() ?? "未选中小怪",
                identity.HasValue ? actors.CanApply(identity.Value) : "请先选中一只普通小怪。",
                session.Entries.ToArray(), status);
        }
        catch (Exception ex)
        {
            status = $"处理失败：{ex.Message}";
            log.Error(ex, "模型替换更新失败");
        }
    }

    private void Execute(Request request)
    {
        if (request.Action == "all")
        {
            session.RestoreAll();
            status = "已请求恢复全部仍在加载范围内的对象。";
            return;
        }
        if (request.Actor is not { } actor) return;
        if (request.Action == "restore")
        {
            status = session.Restore(actor) ? "已请求恢复原模型。" : "该对象没有可恢复的替换。";
            return;
        }
        if (actors.CanApply(actor) is { } reason) throw new InvalidOperationException(reason);
        if (request.Selection < 0 || request.Selection >= catalog.Count) return;
        var appearance = catalog[request.Selection];
        var current = actors.Read(actor);
        session.Apply(actor, actors.Resolve(actor)!.Name.ToString(), appearance.Name,
            NativeActorAccess.Replacement(current, appearance, request.Scale));
        status = $"已请求替换为 Lv.{appearance.Level} {appearance.RankName} 怪「{appearance.Name}」，同步替换模型、名称与显示等级。";
    }

    private void Enqueue(string action, ActorIdentity? actor = null)
        => requests.Enqueue(new(Interlocked.Read(ref epoch), action, actor, selected, scale));

    private void Draw()
    {
        if (!visible || disposed) return;
        var view = snapshot;
        ImGui.SetNextWindowSize(new Vector2(700, 640), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("狩猎模型替换 /hms###HuntModelSwapCN", ref visible))
        {
            ImGui.TextWrapped("将模型、名称和显示等级一起替换为所选狩猎怪物。只影响本机显示；技能与奖励仍按原怪物。");
            ImGui.Separator();
            ImGui.TextUnformatted($"当前目标：{view.TargetName}");
            ImGui.SetNextItemWidth(120);
            if (ImGui.BeginCombo("狩猎级别", rankFilter == 0 ? "全部" : rankFilter switch { 1 => "B", 2 => "A", _ => "S" }))
            {
                foreach (var (value, label) in new[] { (0, "全部"), (3, "S"), (2, "A"), (1, "B") })
                    if (ImGui.Selectable(label, rankFilter == value)) rankFilter = value;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            if (ImGui.BeginCombo("版本", expansionFilter < 0 ? "全部" : $"{expansionFilter + 2}.0"))
            {
                if (ImGui.Selectable("全部", expansionFilter < 0)) expansionFilter = -1;
                foreach (var expansion in catalog.Select(c => (int)c.Expansion).Distinct().OrderDescending())
                    if (ImGui.Selectable($"{expansion + 2}.0", expansion == expansionFilter)) expansionFilter = expansion;
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "搜索怪物名称或地图", ref search, 128);
            if (ImGui.BeginChild("catalog", new Vector2(0, 230), true))
            {
                for (var i = 0; i < catalog.Count; i++)
                {
                    var hunt = catalog[i];
                    if ((rankFilter != 0 && hunt.Rank != rankFilter)
                        || (expansionFilter >= 0 && hunt.Expansion != expansionFilter)
                        || (!hunt.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                            && !hunt.Territory.Contains(search, StringComparison.OrdinalIgnoreCase))) continue;
                    if (ImGui.Selectable($"{hunt.VersionName} {hunt.RankName}  Lv.{hunt.Level} {hunt.Name}  ·  {hunt.Territory}##{i}", selected == i))
                    {
                        selected = i;
                        scale = 1;
                    }
                }
            }
            ImGui.EndChild();
            if (selected >= 0)
                ImGui.TextWrapped($"已选择：Lv.{catalog[selected].Level} {catalog[selected].RankName} 怪「{catalog[selected].Name}」");
            else ImGui.TextUnformatted("请从上方选择一个外观。");
            ImGui.SetNextItemWidth(240);
            ImGui.SliderFloat("体型倍率（1.00 = 真实大小）", ref scale, 0.1f, 3f, "%.2f");
            if (ImGui.Button("设为真实体型（1.00 倍）")) scale = 1;
            ImGui.TextWrapped("默认使用所选怪物原版大小。调整后点击「替换当前目标」应用。");
            var reason = Loading ? "请等待角色进入场景。" : view.CannotApply;
            ImGui.BeginDisabled(reason is not null || selected < 0);
            if (ImGui.Button("替换当前目标")) Enqueue("apply", view.Target);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!view.Target.HasValue || !view.Entries.Any(e => e.Actor == view.Target));
            if (ImGui.Button("恢复当前目标")) Enqueue("restore", view.Target);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(view.Entries.Length == 0);
            if (ImGui.Button("恢复全部")) Enqueue("all");
            ImGui.EndDisabled();
            if (reason is not null) ImGui.TextWrapped(reason);
            ImGui.TextWrapped(view.Status);
            if (ImGui.CollapsingHeader($"已替换对象 ({view.Entries.Length})"))
                foreach (var entry in view.Entries)
                {
                    ImGui.PushID(entry.Actor.Index);
                    if (ImGui.SmallButton("恢复")) Enqueue("restore", entry.Actor);
                    ImGui.SameLine();
                    ImGui.TextWrapped($"Lv.{entry.Original.Level} {entry.OriginalName} → Lv.{entry.Applied.Level} {entry.ReplacementName}"
                        + (entry.WaitingForDraw ? "（重绘中）" : ""));
                    ImGui.PopID();
                }
            ImGui.Separator();
            ImGui.TextWrapped("替换只在当前已加载对象上生效，离开加载范围或切图后结束。插件卸载时恢复仍存在的对象。首版需游戏内验证各怪物的战斗动画。");
        }
        ImGui.End();
    }

    public void Dispose()
    {
        disposed = true;
        commands.RemoveHandler("/hms");
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenMainUi -= Open;
        pluginInterface.UiBuilder.OpenConfigUi -= Open;
        framework.Update -= Update;
        client.TerritoryChanged -= OnTerritoryChanged;
        client.Logout -= OnLogout;
        requests.Clear();
        try
        {
            if (framework.IsInFrameworkUpdateThread) session.Shutdown();
            else framework.RunOnFrameworkThread(session.Shutdown).GetAwaiter().GetResult();
        }
        catch (Exception ex) { log.Error(ex, "卸载时恢复外观失败"); }
    }
}

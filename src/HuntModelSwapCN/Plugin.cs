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
    private readonly IChatGui chat;
    private readonly IDataManager data;
    private readonly NativeActorAccess actors;
    private readonly SwapSession session;
    private readonly ActionPreview preview;
    private readonly LocalPreview localPreview;
    private readonly NativeModelVisibility visibilityAccess;
    private readonly ModelVisibility modelVisibility;
    private readonly NameplatePresentation nameplatePresentation;
    private readonly IReadOnlyList<HuntAppearance> catalog;
    private volatile IReadOnlyDictionary<uint, SkillAnimation> skills = new Dictionary<uint, SkillAnimation>();
    private volatile bool skillsLoaded;
    private volatile string? skillsError;
    private int skillLoadRequested;
    private readonly IReadOnlyDictionary<int, string> modelFamilies;
    private readonly ConcurrentQueue<Request> requests = new();
    private volatile UiSnapshot snapshot = new(null, "未选中小怪", "请先进入游戏。", [], "就绪");
    private bool visible;
    private volatile bool disposed;
    private string search = "";
    private int rankFilter = 3;
    private int expansionFilter = -1;
    private int selected = -1;
    private float scale = 1;
    private float spawnDistance = 8;
    private string status = "就绪";
    private long epoch;
    private string skillSearch = "";
    private bool sameFamily = true;
    private uint selectedSkill;
    private string? lastSkillFilter;
    private SkillAnimation[] skillMatches = [];
    private record Request(long Epoch, string Action, ActorIdentity? Actor, int Selection = -1, float Scale = 1,
        uint SkillId = 0, bool ChatFeedback = false, float Distance = 8, bool HideSelf = false);
    private sealed record HiddenTarget(ActorIdentity Actor, string Name);
    private sealed record UiSnapshot(ActorIdentity? Target, string TargetName, string? CannotApply,
        SwapEntry[] Entries, string Status, ActorIdentity? LocalActor = null,
        bool HideSelf = false, int HiddenCount = 0, string? CannotHide = null, HiddenTarget[]? HiddenTargets = null);

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, ICommandManager commands,
        IClientState client, ICondition condition, IObjectTable objects, IDataManager data,
        ITargetManager targets, IPluginLog log, INamePlateGui nameplates, IChatGui chat)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        this.commands = commands;
        this.client = client;
        this.condition = condition;
        this.targets = targets;
        this.log = log;
        this.chat = chat;
        this.data = data;
        actors = new(objects, nameplates);
        session = new(actors);
        preview = new(session, actors);
        localPreview = new(session, new NativeLocalActorAccess(objects, targets, actors, nameplates));
        visibilityAccess = new(objects);
        modelVisibility = new(visibilityAccess);
        catalog = HuntCatalog.Load(data.GetExcelSheet<TerritoryType>(), data.GetExcelSheet<BNpcName>(),
            data.GetExcelSheet<PlaceName>());
        var models = data.GetExcelSheet<ModelChara>();
        modelFamilies = catalog.Select(h => h.ModelId).Distinct().ToDictionary(id => id,
            id => SkillCatalog.ModelFamily(models.GetRow((uint)id)));
        nameplatePresentation = new(nameplates, session, localPreview, log);
        commands.AddHandler("/hms", new CommandInfo(OnCommand)
            { HelpMessage = "打开狩猎模型替换；/hms show 恢复模型显示；/hms restore 恢复全部；/hms play 技能ID 播放本地动作（实验）。" });
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenMainUi += Open;
        pluginInterface.UiBuilder.OpenConfigUi += Open;
        client.TerritoryChanged += OnTerritoryChanged;
        client.Logout += OnLogout;
        framework.Update += Update;
        log.Information("HuntModelSwapCN 0.1.0.5 initialized. No appearance writes or animation playback have been requested.");
    }

    private void Open()
    {
        visible = true;
        log.Information("HuntModelSwapCN window opened. Opening the window does not perform native appearance writes.");
    }
    private void OnCommand(string command, string arguments)
    {
        if (PreviewCommand.IsPlay(arguments))
        {
            try
            {
                var skillId = PreviewCommand.Parse(arguments);
                requests.Enqueue(new(Interlocked.Read(ref epoch), "play", snapshot.LocalActor ?? snapshot.Target,
                    SkillId: skillId, ChatFeedback: true));
            }
            catch (InvalidOperationException ex) { chat.Print($"[狩猎模型替换] {ex.Message}"); }
        }
        else if (arguments.Trim().Equals("show", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "show-all", null, ChatFeedback: true));
        else if (arguments.Trim().Equals("restore", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "all", null));
        else if (visible) visible = false;
        else Open();
    }

    private void OnTerritoryChanged(uint territory) => ClearSession();
    private void OnLogout(int type, int code) => ClearSession();
    private void ClearSession()
    {
        Interlocked.Increment(ref epoch);
        requests.Clear();
        try { modelVisibility.Reset(); }
        catch (Exception ex) { log.Error(ex, "切换场景时恢复模型显示失败"); }
        session.ForgetDespawned();
        localPreview.ForgetScene();
        modelVisibility.ForgetScene();
        preview.Clear();
        if (!skillsLoaded) Interlocked.Exchange(ref skillLoadRequested, 0);
        snapshot = new(null, "未选中小怪", "正在切换场景。", [], "已结束上一场景的临时替换。");
    }

    private bool Loading => !client.IsLoggedIn || condition[ConditionFlag.BetweenAreas]
        || condition[ConditionFlag.BetweenAreas51];

    private void Update(IFramework _)
    {
        if (disposed) return;
        try
        {
            if (Loading)
            {
                try { modelVisibility.Reset(); }
                finally { localPreview.Clear(); }
                return;
            }
            localPreview.Tick();
            // Complete last frame's redraws before processing new UI requests.
            session.Tick();
            while (requests.TryDequeue(out var request))
            {
                if (request.Epoch != Interlocked.Read(ref epoch)) continue;
                try
                {
                    Execute(request);
                    if (request.ChatFeedback) chat.Print($"[狩猎模型替换] {status}");
                }
                catch (Exception ex)
                {
                    status = ex.Message;
                    log.Error(ex, "模型替换操作失败");
                    if (request.ChatFeedback) chat.Print($"[狩猎模型替换] {status}");
                }
            }
            var npc = targets.Target as IBattleNpc;
            var entries = session.Entries.ToArray();
            // Wait for redraw before recording/restoring a selected object's model visibility.
            var redrawingActors = entries.Where(e => e.WaitingForDraw || e.Restoring).Select(e => e.Actor).ToHashSet();
            modelVisibility.Tick(visibilityAccess.Self, redrawingActors);
            ActorIdentity? identity = npc is { } && npc.IsValid() ? NativeActorAccess.Identity(npc) : null;
            snapshot = new(identity, npc?.Name.ToString() ?? "未选中小怪",
                identity.HasValue ? actors.CanApply(identity.Value) : "请先选中一只普通小怪。",
                entries, status, localPreview.Actor?.Actor,
                modelVisibility.HideSelf, modelVisibility.Count,
                identity.HasValue ? visibilityAccess.CanHide(identity.Value) : "请先选中一只敌对小怪。",
                modelVisibility.ManagedActors.Where(a => a != visibilityAccess.Self)
                    .Select(a => new HiddenTarget(a, visibilityAccess.Name(a))).ToArray());
        }
        catch (Exception ex)
        {
            status = $"处理失败：{ex.Message}";
            log.Error(ex, "模型替换更新失败");
        }
    }

    private void Execute(Request request)
    {
        if (request.Action == "visibility")
        {
            modelVisibility.SetHideSelf(request.HideSelf);
            status = request.HideSelf ? "已隐藏自己的模型。" : "已恢复自己的模型显示状态。";
            return;
        }
        if (request.Action == "show-all")
        {
            modelVisibility.Reset();
            status = "已恢复本插件隐藏的所有模型显示。";
            return;
        }
        if (request.Action == "spawn")
        {
            if (request.Selection < 0 || request.Selection >= catalog.Count)
                throw new InvalidOperationException("请先从列表选择一个狩猎怪物。");
            var hunt = catalog[request.Selection];
            localPreview.Spawn(hunt, request.Scale, request.Distance);
            status = $"已请求在前方 {request.Distance:F1} 米生成「{hunt.Name}」本地预览。无需选中小怪，展开技能动作演示即可播放。";
            return;
        }
        if (request.Action == "remove-local")
        {
            localPreview.Clear();
            status = "已移除本地生成的预览对象。";
            return;
        }
        if (request.Action == "skills")
        {
            LoadSkills();
            status = "技能动作列表已加载。";
            return;
        }
        if (request.Action == "all")
        {
            modelVisibility.Reset();
            localPreview.Clear();
            session.RestoreAll();
            status = "已请求恢复全部仍在加载范围内的对象。";
            return;
        }
        if (request.Actor is not { } actor) throw new InvalidOperationException("请先选中一只普通小怪。");
        if (request.Action == "hide-target")
        {
            if (visibilityAccess.CanHide(actor) is { } hideReason) throw new InvalidOperationException(hideReason);
            modelVisibility.Hide(actor);
            status = $"已请求隐藏「{visibilityAccess.Name(actor)}」的模型，可在隐藏列表中恢复。";
            return;
        }
        if (request.Action == "show-target")
        {
            modelVisibility.Show(actor);
            status = "已请求恢复该小怪原有的模型显示状态。";
            return;
        }
        if (request.Action == "resize")
        {
            if (actors.CanApply(actor) is { } resizeReason) throw new InvalidOperationException(resizeReason);
            session.Resize(actor, request.Scale);
            status = $"已将演示对象的体型倍率设为 {request.Scale:G}。";
            return;
        }
        if (request.Action == "play")
        {
            LoadSkills();
            if (!skills.TryGetValue(request.SkillId, out var skill))
                throw new InvalidOperationException("未找到可播放的 NPC 技能动作，请在窗口中搜索技能名称或 ID。");
            preview.Play(actor, skill, Environment.TickCount64);
            status = $"已请求本地播放「{skill.Name}」（技能 ID {skill.ActionId}）；动作和附带特效需实际确认，不产生真实伤害。";
            return;
        }
        if (request.Action == "restore")
        {
            if (localPreview.Owns(actor))
            {
                localPreview.Clear();
                status = "已移除本地生成的预览对象。";
                return;
            }
            status = session.Restore(actor) ? "已请求恢复原模型。" : "该对象没有可恢复的替换。";
            return;
        }
        if (actors.CanApply(actor) is { } reason) throw new InvalidOperationException(reason);
        if (request.Selection < 0 || request.Selection >= catalog.Count) return;
        var appearance = catalog[request.Selection];
        var current = actors.Read(actor);
        session.Apply(actor, actors.Resolve(actor)!.Name.ToString(), appearance.Name,
            localPreview.PreserveOwnership(actor, NativeActorAccess.Replacement(current, appearance, request.Scale)));
        status = $"已请求替换为 Lv.{appearance.Level} {appearance.RankName} 怪「{appearance.Name}」，同步替换模型、名称与显示等级。";
    }

    private void LoadSkills()
    {
        if (skillsLoaded) return;
        try
        {
            var loaded = SkillCatalog.Load(data.GetExcelSheet<Lumina.Excel.Sheets.Action>(), data.GetExcelSheet<ActionTimeline>());
            skills = loaded;
            skillsError = null;
            skillsLoaded = true;
            log.Information("Loaded {Count} skill animation candidates on explicit preview request.", loaded.Count);
        }
        catch (Exception ex) { skillsError = ex.Message; throw; }
    }

    private void Enqueue(string action, ActorIdentity? actor = null)
        => requests.Enqueue(new(Interlocked.Read(ref epoch), action, actor, selected,
            scale, Distance: spawnDistance));

    private void Draw()
    {
        if (!visible || disposed) return;
        var view = snapshot;
        ImGui.SetNextWindowSize(new Vector2(700, 640), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("狩猎模型替换 /hms###HuntModelSwapCN", ref visible))
        {
            ImGui.TextWrapped("选择狩猎怪物后，可在面前生成本地预览，或替换当前小怪的外观。只影响本机显示。");
            var hideSelf = view.HideSelf;
            ImGui.BeginDisabled(Loading);
            if (ImGui.Checkbox("隐藏自己模型", ref hideSelf))
                requests.Enqueue(new(Interlocked.Read(ref epoch), "visibility", null, HideSelf: hideSelf));
            ImGui.EndDisabled();
            ImGui.TextUnformatted($"当前目标：{view.TargetName}");
            var hiddenTargets = view.HiddenTargets ?? [];
            var targetHidden = view.Target.HasValue && hiddenTargets.Any(t => t.Actor == view.Target);
            ImGui.BeginDisabled(Loading || !view.Target.HasValue || view.CannotHide is not null || targetHidden);
            if (ImGui.Button("隐藏当前目标")) Enqueue("hide-target", view.Target);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(Loading || !targetHidden);
            if (ImGui.Button("恢复当前目标显示")) Enqueue("show-target", view.Target);
            ImGui.EndDisabled();
            if (view.CannotHide is { } hideReason) ImGui.TextWrapped(hideReason);
            if (ImGui.Button("恢复全部显示 /hms show")) Enqueue("show-all");
            ImGui.SameLine();
            ImGui.TextUnformatted($"已管理隐藏对象：{view.HiddenCount}");
            ImGui.TextWrapped("先选中一只敌对小怪，再点击隐藏。可逐只隐藏；隐藏后从下方列表恢复，无需再次点中模型。切图、登出自动恢复。仅隐藏模型，名牌和独立技能特效仍可能显示。");
            if (hiddenTargets.Length > 0 && ImGui.CollapsingHeader($"单独隐藏的小怪 ({hiddenTargets.Length})", ImGuiTreeNodeFlags.DefaultOpen))
                foreach (var hidden in hiddenTargets)
                {
                    ImGui.PushID($"hidden-{hidden.Actor.Index}-{hidden.Actor.ObjectId}");
                    if (ImGui.SmallButton("恢复显示")) Enqueue("show-target", hidden.Actor);
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"{hidden.Name}（对象 {hidden.Actor.Index}）");
                    ImGui.PopID();
                }
            ImGui.Separator();
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
                        scale = hunt.Scale;
                    }
                }
            }
            ImGui.EndChild();
            if (selected >= 0)
                ImGui.TextWrapped($"已选择：Lv.{catalog[selected].Level} {catalog[selected].RankName} 怪「{catalog[selected].Name}」");
            else ImGui.TextUnformatted("请从上方选择一个外观。");
            ImGui.BeginDisabled(selected < 0);
            ImGui.SetNextItemWidth(240);
            ImGui.InputFloat("体型倍率", ref scale, 0.1f, 1f, "%.3f");
            ImGui.SameLine();
            if (ImGui.Button("恢复所选怪物原版倍率") && selected >= 0) scale = catalog[selected].Scale;
            ImGui.EndDisabled();
            if (selected >= 0) ImGui.TextWrapped($"「{catalog[selected].Name}」原版倍率：{catalog[selected].Scale:G}。输入值直接作为模型倍率，不再额外乘原版体型。");
            var sizeValid = float.IsFinite(scale) && scale > 0;
            if (!sizeValid) ImGui.TextWrapped("请输入大于 0 的有限倍率。");
            var resizeActor = view.LocalActor ?? view.Target;
            var resizeEntry = view.Entries.FirstOrDefault(e => e.Actor == resizeActor);
            ImGui.BeginDisabled(Loading || !sizeValid || resizeEntry is null || resizeEntry.WaitingForDraw || resizeEntry.Restoring);
            if (ImGui.Button("应用体型到演示对象")) Enqueue("resize", resizeActor);
            ImGui.EndDisabled();
            if (resizeEntry is not null) ImGui.TextWrapped($"体型调整对象：{resizeEntry.ReplacementName}，当前倍率 {resizeEntry.Applied.ModelScale:G}。有本地预览时优先调整它。");
            ImGui.SetNextItemWidth(240);
            ImGui.SliderFloat("前方生成距离（米）", ref spawnDistance, 2, 30, "%.1f");
            ImGui.BeginDisabled(Loading || selected < 0 || !sizeValid);
            if (ImGui.Button("在我面前生成（本地预览）")) Enqueue("spawn");
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!view.LocalActor.HasValue);
            if (ImGui.Button("移除预览")) Enqueue("remove-local");
            ImGui.EndDisabled();
            ImGui.TextWrapped("无需选中小怪。只保留一只本地生成对象；再次生成会替换它。技能演示优先控制此对象。");
            ImGui.TextWrapped("本地预览隐藏头顶血条，名字、等级和精英头标使用原生敌对怪名牌样式。");
            var reason = Loading ? "请等待角色进入场景。" : view.CannotApply;
            ImGui.BeginDisabled(reason is not null || selected < 0 || !sizeValid);
            if (ImGui.Button("替换当前目标")) Enqueue("apply", view.Target);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!view.Target.HasValue || !view.Entries.Any(e => e.Actor == view.Target));
            if (ImGui.Button("恢复当前目标")) Enqueue("restore", view.Target);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(view.Entries.Length == 0 && !view.HideSelf && view.HiddenCount == 0 && hiddenTargets.Length == 0);
            if (ImGui.Button("恢复全部")) Enqueue("all");
            ImGui.EndDisabled();
            if (reason is not null) ImGui.TextWrapped(reason);
            ImGui.TextWrapped(view.Status);
            DrawSkillPreview(view);
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

    private void DrawSkillPreview(UiSnapshot view)
    {
        if (!ImGui.CollapsingHeader("技能动作演示（实验）")) return;
        if (!skillsLoaded)
        {
            if (skillsError is { } error)
            {
                ImGui.TextWrapped($"技能列表读取失败：{error}");
                if (ImGui.Button("重试加载技能列表"))
                {
                    skillsError = null;
                    Enqueue("skills");
                }
            }
            else
            {
                if (Interlocked.CompareExchange(ref skillLoadRequested, 1, 0) == 0) Enqueue("skills");
                ImGui.TextWrapped("正在读取技能动作列表，请稍候。首次展开时加载。");
            }
            return;
        }
        ImGui.TextWrapped("仅播放本机动作，不改变真实技能或伤害。附带特效取决于动作资源；原怪移动、攻击可能中断演示。");
        var playActor = view.LocalActor ?? view.Target;
        var entry = view.Entries.FirstOrDefault(e => e.Actor == playActor);
        ImGui.TextUnformatted($"演示对象：{(view.LocalActor.HasValue ? entry?.ReplacementName + "（本地生成）" : view.TargetName)}");
        var family = entry is not null && modelFamilies.TryGetValue(entry.Applied.ModelId, out var key) ? key : "";
        var reason = Loading ? "请等待角色进入场景。" : (view.LocalActor.HasValue ? null : view.CannotApply)
            ?? (entry is null ? "请先生成本地预览，或替换当前目标的模型。" : entry.WaitingForDraw || entry.Restoring ? "请等待对象完成重绘。" : null);
        var hunt = entry is null ? null : catalog.FirstOrDefault(h => h.ModelId == entry.Applied.ModelId
            && h.Name == entry.ReplacementName);
        if (hunt is not null)
        {
            var presets = HuntSkillPresets.For(hunt.NameId, skills).ToArray();
            if (presets.Length > 0)
            {
                ImGui.TextUnformatted($"{hunt.Name} · 战斗技能快捷播放");
                ImGui.BeginDisabled(reason is not null);
                foreach (var skill in presets)
                    if (ImGui.Button($"{skill.Name}  /hms play {skill.ActionId}"))
                        requests.Enqueue(new(Interlocked.Read(ref epoch), "play", playActor, SkillId: skill.ActionId));
                ImGui.EndDisabled();
                ImGui.TextWrapped("技能归属依据灰机 Wiki 资料；仅演示单次动作，不模拟吸气顺序、转向、击退或异常状态。");
                ImGui.Separator();
            }
        }
        ImGui.Checkbox("仅显示当前模型同资源的候选动作", ref sameFamily);
        if (sameFamily && family.Length == 0)
            ImGui.TextWrapped("请先生成预览或替换当前目标。人形等无法推断资源的模型可取消此筛选，再搜索技能。");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##skillSearch", "搜索技能名称或技能 ID", ref skillSearch, 128);
        var filter = $"{sameFamily}|{family}|{skillSearch}";
        if (filter != lastSkillFilter)
        {
            lastSkillFilter = filter;
            selectedSkill = 0;
            skillMatches = skills.Values.Where(s => (!sameFamily || (family.Length > 0 && (s.Family == family || s.Family == "[SKL_ID]")))
                && s.Matches(skillSearch)).Take(151).ToArray();
        }
        if (ImGui.BeginChild("skillList", new Vector2(0, 140), true))
            foreach (var skill in skillMatches.Take(150))
                if (ImGui.Selectable($"{skill.Name}  ·  ID {skill.ActionId}##skill{skill.ActionId}", selectedSkill == skill.ActionId))
                    selectedSkill = skill.ActionId;
        ImGui.EndChild();
        if (skillMatches.Length > 150) ImGui.TextWrapped("最多显示前 150 项，请搜索以缩小范围。");
        ImGui.TextWrapped("筛选包含按当前骨架解析的通用动作，只提供候选，不代表这只狩猎怪实际拥有该技能。");
        ImGui.BeginDisabled(reason is not null || selectedSkill == 0);
        if (ImGui.Button("播放选中技能动作"))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "play", playActor, SkillId: selectedSkill));
        ImGui.EndDisabled();
        if (reason is not null) ImGui.TextWrapped(reason);
        if (skills.TryGetValue(selectedSkill, out var chosen))
            ImGui.TextWrapped($"聊天指令：/hms play {chosen.ActionId}（技能 ID）");
        ImGui.TextWrapped("每次播放间隔至少 0.75 秒。若动作异常，可使用上方「恢复当前目标」。");
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
        nameplatePresentation.Dispose();
        try
        {
            void Shutdown()
            {
                try { modelVisibility.Reset(); }
                finally
                {
                    try { localPreview.Clear(); }
                    finally { session.Shutdown(); }
                }
            }
            if (framework.IsInFrameworkUpdateThread) Shutdown();
            else framework.RunOnFrameworkThread(Shutdown).GetAwaiter().GetResult();
        }
        catch (Exception ex) { log.Error(ex, "卸载时恢复外观失败"); }
    }
}

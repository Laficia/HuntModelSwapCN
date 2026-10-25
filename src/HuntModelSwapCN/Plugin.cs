using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
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
    private readonly BufruSequence sequence;
    private readonly NativeAnimationPause pauseAccess;
    private readonly AnimationPause animationPause;
    private readonly ModelRotation modelRotation;
    private readonly DialoguePreview dialoguePreview;
    private readonly HuntDialogueCatalog dialogueCatalog;
    private readonly Configuration configuration;
    private volatile HuntDialogue[] dialogueLines = [];
    private volatile bool captureHuntDialogue;
    private bool dialogueLoaded;
    private readonly ConcurrentQueue<(long Epoch, uint NameId, string Text)> incomingDialogue = new();
    private readonly NativeModelVisibility visibilityAccess;
    private readonly ModelVisibility modelVisibility;
    private readonly NameplatePresentation nameplatePresentation;
    private readonly IReadOnlyList<HuntAppearance> catalog;
    private readonly MapChoice[] mapChoices;
    private volatile IReadOnlyDictionary<uint, SkillAnimation> skills = new Dictionary<uint, SkillAnimation>();
    private volatile bool skillsLoaded;
    private volatile string? skillsError;
    private int skillLoadRequested;
    private readonly IReadOnlyDictionary<int, string> modelFamilies;
    private readonly ConcurrentQueue<Request> requests = new();
    private volatile UiSnapshot snapshot = new(null, "未选中小怪", "请先进入游戏。", [], "就绪");
    private bool visible;
    private bool floatingControls;
    private volatile ActorSelection? controlSelection;
    private ActorIdentity? rotationInputActor;
    private float rotationDegrees;
    private volatile bool disposed;
    private string search = "";
    private int rankFilter;
    private int expansionFilter = -1;
    private uint territoryFilter;
    private int selected = -1;
    private float scale = 1;
    private float spawnDistance = 8;
    private float sequenceHoldSeconds = 2;
    private string status = "就绪";
    private long epoch;
    private string skillSearch = "";
    private bool sameFamily = true;
    private bool showSkillVariants;
    private uint selectedSkill;
    private string? lastSkillFilter;
    private SkillAnimation[] skillMatches = [];
    private record Request(long Epoch, string Action, ActorIdentity? Actor, int Selection = -1, float Scale = 1,
        uint SkillId = 0, bool ChatFeedback = false, float Distance = 8, bool HideSelf = false, int HoldMs = 2000,
        bool HideAllMonsters = false, float AngleDegrees = 0, uint DialogueNameId = 0,
        string Text = "", bool Enabled = false);
    private sealed record HiddenTarget(ActorIdentity Actor, string Name);
    private sealed record ActorSelection(ActorIdentity Actor);
    private sealed record ActorHeading(ActorIdentity Actor, float? Degrees);
    private sealed record MapChoice(uint TerritoryId, uint Expansion, string Name);
    private sealed record UiSnapshot(ActorIdentity? Target, string TargetName, string? CannotApply,
        SwapEntry[] Entries, string Status, ActorIdentity? LocalActor = null,
        bool HideSelf = false, int HiddenCount = 0, string? CannotHide = null, HiddenTarget[]? HiddenTargets = null,
        bool ComboRunning = false, string ComboStatus = "", string ComboDirections = "", float ComboRemaining = 0,
        bool HideAllMonsters = false, ActorIdentity[]? PausedActors = null, ActorHeading[]? Headings = null,
        ActorIdentity[]? RotationActors = null, bool DialogueRunning = false);

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, ICommandManager commands,
        IClientState client, ICondition condition, IObjectTable objects, IDataManager data,
        ITargetManager targets, IPluginLog log, INamePlateGui nameplates, IChatGui chat, IGameInteropProvider interop)
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
        catalog = HuntCatalog.Load(data.GetExcelSheet<TerritoryType>(), data.GetExcelSheet<BNpcName>(),
            data.GetExcelSheet<PlaceName>());
        actors = new(objects, nameplates);
        session = new(actors);
        preview = new(session, actors);
        localPreview = new(session, new NativeLocalActorAccess(objects, targets, actors, nameplates), entry =>
            catalog.FirstOrDefault(h => h.ModelId == entry.Applied.ModelId && h.Name == entry.ReplacementName)?.IsAggressive);
        pauseAccess = new(actors, interop, log);
        animationPause = new(session, pauseAccess, actor => !localPreview.Owns(actor) || localPreview.OwnsLive(actor));
        pauseAccess.ShouldPause = animationPause.IsPausedAddress;
        pauseAccess.CanControl = actor => session.Entries.Any(e => e.Actor == actor)
            && (!localPreview.Owns(actor) || localPreview.OwnsLive(actor));
        modelRotation = new(session, actors, actor => !localPreview.Owns(actor) || localPreview.OwnsLive(actor));
        dialoguePreview = new(session, actors, actor => !localPreview.Owns(actor) || localPreview.OwnsLive(actor));
        sequence = new(session, preview, actors, actor => !localPreview.Owns(actor) || localPreview.OwnsLive(actor),
            animationPause.IsPaused);
        visibilityAccess = new(objects);
        modelVisibility = new(visibilityAccess);
        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new();
        captureHuntDialogue = configuration.CaptureHuntDialogue;
        dialogueCatalog = new(catalog);
        foreach (var line in (configuration.CapturedDialogue ?? []).Take(47 * HuntDialogueCatalog.MaxPerHunt))
            try { if (line is not null) dialogueCatalog.Capture(line.NameId, line.Text); }
            catch (InvalidOperationException) { /* Ignore invalid saved text, never pass it to native UI. */ }
        dialogueLines = dialogueCatalog.All;
        mapChoices = catalog.GroupBy(h => h.TerritoryId)
            .Select(g => new MapChoice(g.Key, g.First().Expansion, g.First().Territory))
            .OrderByDescending(m => m.Expansion).ThenBy(m => m.TerritoryId).ToArray();
        var models = data.GetExcelSheet<ModelChara>();
        modelFamilies = catalog.Select(h => h.ModelId).Distinct().ToDictionary(id => id,
            id => SkillCatalog.ModelFamily(models.GetRow((uint)id)));
        nameplatePresentation = new(nameplates, session, localPreview, log);
        commands.AddHandler("/hms", new CommandInfo(OnCommand)
            { HelpMessage = "打开狩猎模型替换；/hms controls 悬浮控制；/hms pause 暂停；/hms resume 恢复；/hms rotate 角度 水平旋转；/hms show 恢复显示；/hms restore 恢复全部；/hms play 技能ID；/hms combo 布弗鲁连招；/hms stop 停止连招；/hms talk 阿提卡斯四句气泡；/hms stoptalk 停止台词。" });
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenMainUi += Open;
        pluginInterface.UiBuilder.OpenConfigUi += Open;
        client.TerritoryChanged += OnTerritoryChanged;
        client.Logout += OnLogout;
        chat.ChatMessage += OnNpcDialogue;
        framework.Update += Update;
        log.Information("HuntModelSwapCN 0.1.0.13 initialized. No appearance writes, native bubbles or animation playback have been requested.");
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
                requests.Enqueue(new(Interlocked.Read(ref epoch), "play", ControlActor(snapshot),
                    SkillId: skillId, ChatFeedback: true));
            }
            catch (InvalidOperationException ex) { chat.Print($"[狩猎模型替换] {ex.Message}"); }
        }
        else if (arguments.Trim().Equals("combo", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "start-combo", ControlActor(snapshot),
                ChatFeedback: true, HoldMs: (int)(sequenceHoldSeconds * 1000)));
        else if (arguments.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "stop-combo", null, ChatFeedback: true));
        else if (arguments.Trim().Equals("show", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "show-all", null, ChatFeedback: true));
        else if (arguments.Trim().Equals("pause", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "pause", ControlActor(snapshot), ChatFeedback: true));
        else if (arguments.Trim().Equals("resume", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "resume", ControlActor(snapshot), ChatFeedback: true));
        else if (arguments.Trim().Equals("controls", StringComparison.OrdinalIgnoreCase)) floatingControls = !floatingControls;
        else if (arguments.Trim().Equals("talk", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "dialogue-sequence", ControlActor(snapshot), ChatFeedback: true));
        else if (arguments.Trim().Equals("stoptalk", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "dialogue-stop", null, ChatFeedback: true));
        else if (arguments.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Equals("rotate", StringComparison.OrdinalIgnoreCase) == true)
        {
            var parts = arguments.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var degrees) && float.IsFinite(degrees))
                requests.Enqueue(new(Interlocked.Read(ref epoch), "rotate", ControlActor(snapshot), ChatFeedback: true, AngleDegrees: degrees));
            else chat.Print("[狩猎模型替换] 用法：/hms rotate 90（水平角度，0～360°）。");
        }
        else if (arguments.Trim().Equals("restore", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "all", null));
        else if (visible) visible = false;
        else Open();
    }

    private void OnTerritoryChanged(uint territory) => ClearSession();
    private void OnNpcDialogue(IChatMessage message)
    {
        if (disposed || !captureHuntDialogue || incomingDialogue.Count >= 128
            || message.LogKind is not (XivChatType.NPCDialogue or XivChatType.NPCDialogueAnnouncements)) return;
        try
        {
            var nameId = dialogueCatalog.Speaker(message.OriginalSender.ExtractText());
            if (nameId is not { } id) return;
            var text = HuntDialogueCatalog.Normalize(message.OriginalMessage.ExtractText());
            incomingDialogue.Enqueue((Interlocked.Read(ref epoch), id, text));
        }
        catch (Exception ex) { log.Warning(ex, "读取 S 怪台词失败"); }
    }

    private void SaveDialogue()
    {
        configuration.CaptureHuntDialogue = captureHuntDialogue;
        configuration.CapturedDialogue = dialogueCatalog.All.Where(l => l.RowId == 0).ToList();
        try { pluginInterface.SavePluginConfig(configuration); }
        catch (Exception ex) { log.Error(ex, "保存 S 怪台词失败；当前会话仍可使用"); }
    }

    private void LoadDialogue()
    {
        if (dialogueLoaded) return;
        dialogueCatalog.LoadBuiltIns(data.GetExcelSheet<NpcYell>());
        dialogueLines = dialogueCatalog.All;
        dialogueLoaded = true;
    }

    private void OnLogout(int type, int code) => ClearSession();
    private void ClearSession()
    {
        Interlocked.Increment(ref epoch);
        requests.Clear();
        incomingDialogue.Clear();
        dialoguePreview.ForgetScene();
        sequence.ForgetScene();
        animationPause.ForgetScene();
        modelRotation.Clear();
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
                dialoguePreview.ForgetScene();
                incomingDialogue.Clear();
                sequence.ForgetScene();
                modelRotation.Clear();
                try { animationPause.Reset(); }
                finally
                {
                    try { modelVisibility.Reset(); }
                    finally { localPreview.Clear(); }
                }
                return;
            }
            localPreview.Tick();
            // Complete last frame's redraws before processing new UI requests.
            session.Tick();
            var captured = false;
            while (incomingDialogue.TryDequeue(out var spoken))
                if (captureHuntDialogue && spoken.Epoch == Interlocked.Read(ref epoch) && actors.HasRealHunt(spoken.NameId))
                    captured |= dialogueCatalog.Capture(spoken.NameId, spoken.Text);
            if (captured) { dialogueLines = dialogueCatalog.All; SaveDialogue(); }
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
            try { animationPause.Tick(); }
            catch (Exception ex) { status = "暂停动画维护失败，可点击恢复动作。"; log.Error(ex, status); }
            pauseAccess.PrunePending();
            try { dialoguePreview.Tick(Environment.TickCount64); }
            catch (Exception ex) { dialoguePreview.StopSequence(); status = "台词播放已中断，请重新选择演示对象。"; log.Error(ex, status); }
            try { modelRotation.Tick(); }
            catch (Exception ex) { status = "旋转对象已不可用，已解除固定朝向。"; log.Error(ex, status); }
            var wasRunning = sequence.Running;
            try { sequence.Tick(Environment.TickCount64); }
            catch (Exception ex) { status = sequence.Status; log.Error(ex, "布弗鲁连招已中断"); }
            if (wasRunning && !sequence.Running) status = sequence.Status;
            var npc = targets.Target as IBattleNpc;
            var entries = session.Entries.ToArray();
            // Wait for redraw before recording/restoring a selected object's model visibility.
            var redrawingActors = entries.Where(e => e.WaitingForDraw || e.Restoring).Select(e => e.Actor).ToHashSet();
            var protectedActors = entries.Where(e => !e.Restoring).Select(e => e.Actor).ToHashSet();
            if (localPreview.Actor is { } ownedPreview) protectedActors.Add(ownedPreview.Actor);
            modelVisibility.Tick(visibilityAccess.Self, redrawingActors, modelVisibility.HideAllMonsters
                ? visibilityAccess.BulkCandidates(protectedActors) : null);
            ActorIdentity? identity = npc is { } && npc.IsValid() ? NativeActorAccess.Identity(npc) : null;
            snapshot = new(identity, npc?.Name.ToString() ?? "未选中小怪",
                identity.HasValue ? actors.CanApply(identity.Value) : "请先选中一只普通小怪。",
                entries, status, localPreview.Actor?.Actor,
                modelVisibility.HideSelf, modelVisibility.Count,
                identity.HasValue ? visibilityAccess.CanHide(identity.Value) : "请先选中一只战斗小怪（主动或被动均可）。",
                modelVisibility.ManagedActors.Where(a => a != visibilityAccess.Self)
                    .Select(a => new HiddenTarget(a, visibilityAccess.Name(a))).ToArray(),
                sequence.Running, sequence.Status, sequence.DirectionOrder, sequence.RemainingSeconds(Environment.TickCount64),
                modelVisibility.HideAllMonsters, animationPause.Actors.ToArray(),
                entries.Select(e => new ActorHeading(e.Actor, actors.Heading(e.Actor, e.Applied.ModelId))).ToArray(),
                modelRotation.Actors.ToArray(), dialoguePreview.Running);
        }
        catch (Exception ex)
        {
            status = $"处理失败：{ex.Message}";
            log.Error(ex, "模型替换更新失败");
        }
    }

    private void Execute(Request request)
    {
        if (request.Action == "dialogue-load") { LoadDialogue(); status = "已加载已确认的台词与实战收集记录。"; return; }
        if (request.Action == "dialogue-capture")
        {
            captureHuntDialogue = request.Enabled;
            incomingDialogue.Clear();
            SaveDialogue();
            status = captureHuntDialogue ? "已开启收集加载范围内真实 S 怪的 NPC 台词。" : "已停止收集 S 怪台词。";
            return;
        }
        if (request.Action == "dialogue-stop")
        {
            dialoguePreview.Reset(); status = "已停止台词序列并关闭本插件播放的气泡。"; return;
        }
        if (request.Action == "all") dialoguePreview.Reset();
        else if (request.Action is "spawn" or "remove-local" && localPreview.Actor is { } speakingPreview)
            dialoguePreview.Clear(speakingPreview.Actor);
        else if (request.Action is "apply" or "restore" && request.Actor is { } speakingActor)
            dialoguePreview.Clear(speakingActor);
        if (request.Action is "play" or "start-combo" && request.Actor is { } playingActor && animationPause.IsPaused(playingActor))
            throw new InvalidOperationException("演示对象已暂停，请先点击「恢复动作」或输入 /hms resume。");
        // Restore saved speeds before any operation can redraw/delete/change the model.
        if (request.Action == "all") animationPause.Reset();
        else if (request.Action is "spawn" or "remove-local" && localPreview.Actor is { } oldPreview)
            animationPause.Resume(oldPreview.Actor);
        else if (request.Action is "apply" or "restore" && request.Actor is { } changingActor)
            animationPause.Resume(changingActor);
        if (request.Action == "all") modelRotation.Clear();
        else if (request.Action is "spawn" or "remove-local" && localPreview.Actor is { } rotatingPreview)
            modelRotation.Release(rotatingPreview.Actor);
        else if (request.Action is "apply" or "restore" or "start-combo" && request.Actor is { } rotatingActor)
            modelRotation.Release(rotatingActor);
        if (request.Action is "spawn" or "remove-local" or "all" or "apply" or "restore" or "resize" or "play")
            sequence.Stop("已取消连招，正在执行新的演示操作。");
        if (request.Action == "stop-combo")
        {
            sequence.Stop();
            status = sequence.Status;
            return;
        }
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
        if (request.Action == "resume-all")
        {
            animationPause.Reset();
            status = "已恢复所有暂停对象的原有动画速度。";
            return;
        }
        if (request.Action == "hide-all-monsters")
        {
            modelVisibility.SetHideAllMonsters(request.HideAllMonsters);
            status = request.HideAllMonsters ? "已开启隐藏所有小怪，保留预览和替换演示对象。"
                : "已关闭隐藏所有小怪；单独隐藏的小怪仍保持隐藏。";
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
        if (request.Action is "dialogue-play" or "dialogue-sequence")
        {
            LoadDialogue();
            var entry = session.Entries.FirstOrDefault(e => e.Actor == actor);
            var hunt = catalog.FirstOrDefault(h => h.ModelId == entry?.Applied.ModelId && h.Name == entry.ReplacementName);
            if (hunt is null || hunt.Rank != 3) throw new InvalidOperationException("请先生成或替换一只 S 怪，并选为控制对象。");
            if (request.Action == "dialogue-sequence")
            {
                if (hunt.NameId != HuntDialogueCatalog.Atticus) throw new InvalidOperationException("/hms talk 当前演示阿提卡斯的四句台词；其他 S 怪可在台词菜单逐句播放。");
                var texts = new[] { "我们三头……！\n三位一体，一起干掉你！", "必、必杀……", "三重……", "吐息？" };
                if (texts.Any(t => !dialogueLines.Any(l => l.NameId == hunt.NameId && l.Text == t)))
                    throw new InvalidOperationException("当前客户端的阿提卡斯台词未通过核对。");
                dialoguePreview.Start(actor, texts.Select((t, i) => new DialogueStep(t, i == 0 ? 5 : 3,
                    i == 0 ? 6000 : i == 1 ? 3000 : 4000)).ToArray(), Environment.TickCount64);
                status = "已开始阿提卡斯四句台词气泡，按提供的 0／6／9／13 秒间隔演示。";
            }
            else
            {
                var line = dialogueLines.FirstOrDefault(l => l.NameId == hunt.NameId && l.NameId == request.DialogueNameId && l.Text == request.Text)
                    ?? throw new InvalidOperationException("台词与当前演示对象不匹配。");
                dialoguePreview.StopSequence();
                dialoguePreview.Play(actor, line.Text, line.Seconds);
                status = "已在演示模型上播放本地 NPC 台词气泡。";
            }
            return;
        }
        if (request.Action == "pause")
        {
            animationPause.Pause(actor);
            status = "已暂停该演示对象的全部动画，连招计时与转向也会暂停。";
            return;
        }
        if (request.Action == "resume")
        {
            status = animationPause.Resume(actor, repairIfNotPaused: true) ? "已刷新并恢复该对象的动画播放。" : "对象已不可用，未恢复旧速度。";
            return;
        }
        if (request.Action == "rotate")
        {
            sequence.Stop("已停止自动连招，改为手动旋转。");
            modelRotation.Set(actor, request.AngleDegrees);
            status = "已设置并保持演示模型的水平朝向，暂停状态不变。";
            return;
        }
        if (request.Action == "release-rotation")
        {
            modelRotation.Release(actor);
            status = "已解除固定朝向。";
            return;
        }
        if (request.Action == "start-combo")
        {
            LoadSkills();
            sequence.Start(actor, skills, Enumerable.Range(0, 5).Select(_ => Random.Shared.Next(4)).ToArray(),
                request.HoldMs, Environment.TickCount64);
            status = "已开始布弗鲁五次吸气 → 同顺序五次喷嚏的本地连招。";
            return;
        }
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
            scale, Distance: spawnDistance, HoldMs: (int)(sequenceHoldSeconds * 1000)));

    private void Draw()
    {
        if (disposed) return;
        var view = snapshot;
        if (floatingControls)
        {
            ImGui.SetNextWindowSize(new Vector2(480, 340), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("模型悬浮控制###HuntModelSwapControls", ref floatingControls))
            {
                DrawModelControls(view);
                ImGui.TextWrapped(view.Status);
            }
            ImGui.End();
        }
        if (!visible) return;
        ImGui.SetNextWindowSize(new Vector2(700, 640), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("狩猎模型替换 /hms###HuntModelSwapCN", ref visible))
        {
            ImGui.TextWrapped("选择狩猎怪物后，可在面前生成本地预览，或替换当前小怪的外观。只影响本机显示。");
            ImGui.Checkbox("显示悬浮控制窗 /hms controls", ref floatingControls);
            DrawModelControls(view);
            ImGui.Separator();
            var hideSelf = view.HideSelf;
            ImGui.BeginDisabled(Loading);
            if (ImGui.Checkbox("隐藏自己模型", ref hideSelf))
                requests.Enqueue(new(Interlocked.Read(ref epoch), "visibility", null, HideSelf: hideSelf));
            var hideAllMonsters = view.HideAllMonsters;
            if (ImGui.Checkbox("隐藏所有小怪（保留演示对象）", ref hideAllMonsters))
                requests.Enqueue(new(Interlocked.Read(ref epoch), "hide-all-monsters", null, HideAllMonsters: hideAllMonsters));
            ImGui.EndDisabled();
            if (view.HideAllMonsters)
                ImGui.TextWrapped("持续隐藏加载范围内的主动与被动战斗小怪，保留生成预览和已替换的演示对象。列表中单独恢复的小怪在本次开启期间保持显示；重新开启批量隐藏后重置。");
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
            ImGui.TextWrapped("先选中一只战斗小怪，再点击隐藏，主动与被动小怪均可。可逐只隐藏；隐藏后从下方列表恢复，无需再次点中模型。切图、登出自动恢复。仅隐藏模型，名牌和独立技能特效仍可能显示。");
            if (hiddenTargets.Length > 0 && ImGui.CollapsingHeader($"隐藏的小怪（可逐只恢复）({hiddenTargets.Length})", ImGuiTreeNodeFlags.DefaultOpen))
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
            if (ImGui.SmallButton("全选级别")) rankFilter = 0;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            if (ImGui.BeginCombo("版本", expansionFilter < 0 ? "全部" : $"{expansionFilter + 2}.0"))
            {
                if (ImGui.Selectable("全部", expansionFilter < 0)) expansionFilter = -1;
                foreach (var expansion in catalog.Select(c => (int)c.Expansion).Distinct().OrderDescending())
                    if (ImGui.Selectable($"{expansion + 2}.0", expansion == expansionFilter)) expansionFilter = expansion;
                ImGui.EndCombo();
            }
            var availableMaps = mapChoices.Where(m => expansionFilter < 0 || m.Expansion == expansionFilter);
            // A previous version's map must not silently filter out every hunt.
            if (territoryFilter != 0 && !availableMaps.Any(m => m.TerritoryId == territoryFilter)) territoryFilter = 0;
            var chosenMap = mapChoices.FirstOrDefault(m => m.TerritoryId == territoryFilter);
            ImGui.SetNextItemWidth(300);
            if (ImGui.BeginCombo("地图", chosenMap?.Name ?? "全部地图"))
            {
                if (ImGui.Selectable("全部地图", territoryFilter == 0)) territoryFilter = 0;
                foreach (var map in availableMaps)
                    if (ImGui.Selectable($"{(expansionFilter < 0 ? $"{map.Expansion + 2}.0 · " : "")}{map.Name}##map{map.TerritoryId}",
                        territoryFilter == map.TerritoryId)) territoryFilter = map.TerritoryId;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("全部地图")) territoryFilter = 0;
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "搜索怪物名称或地图", ref search, 128);
            if (ImGui.BeginChild("catalog", new Vector2(0, 230), true))
            {
                for (var i = 0; i < catalog.Count; i++)
                {
                    var hunt = catalog[i];
                    if ((rankFilter != 0 && hunt.Rank != rankFilter)
                        || (expansionFilter >= 0 && hunt.Expansion != expansionFilter)
                        || (territoryFilter != 0 && hunt.TerritoryId != territoryFilter)
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
            var resizeActor = ControlActor(view);
            var resizeEntry = view.Entries.FirstOrDefault(e => e.Actor == resizeActor);
            ImGui.BeginDisabled(Loading || !sizeValid || resizeEntry is null || resizeEntry.WaitingForDraw || resizeEntry.Restoring);
            if (ImGui.Button("应用体型到演示对象")) Enqueue("resize", resizeActor);
            ImGui.EndDisabled();
            if (resizeEntry is not null) ImGui.TextWrapped($"体型调整对象：{resizeEntry.ReplacementName}，当前倍率 {resizeEntry.Applied.ModelScale:G}。可在上方控制对象中切换。");
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
            ImGui.TextWrapped("生成的 B 怪使用蓝色被动精英头标；A/S 怪使用主动精英头标。颜色分类随当前演示外观更新。");
            var reason = Loading ? "请等待角色进入场景。" : view.CannotApply;
            ImGui.BeginDisabled(reason is not null || selected < 0 || !sizeValid);
            if (ImGui.Button("替换当前目标")) Enqueue("apply", view.Target);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!view.Target.HasValue || !view.Entries.Any(e => e.Actor == view.Target));
            if (ImGui.Button("恢复当前目标")) Enqueue("restore", view.Target);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(view.Entries.Length == 0 && !view.HideSelf && !view.HideAllMonsters
                && view.HiddenCount == 0 && hiddenTargets.Length == 0);
            if (ImGui.Button("恢复全部")) Enqueue("all");
            ImGui.EndDisabled();
            if (reason is not null) ImGui.TextWrapped(reason);
            ImGui.TextWrapped(view.Status);
            DrawDialoguePreview(view);
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

    private ActorIdentity? ControlActor(UiSnapshot view) => controlSelection is { } chosen
        && view.Entries.Any(e => e.Actor == chosen.Actor) ? chosen.Actor : view.LocalActor ?? view.Target;

    private void DrawDialoguePreview(UiSnapshot view)
    {
        if (!ImGui.CollapsingHeader("S 怪台词与原生气泡")) return;
        var capture = captureHuntDialogue;
        if (ImGui.Checkbox("收集真实 S 怪的 NPC 台词（保存到本地）", ref capture))
            requests.Enqueue(new(Interlocked.Read(ref epoch), "dialogue-capture", null, Enabled: capture));
        ImGui.TextWrapped("覆盖列表中的 47 只 S 怪；只收集 NPC 对话频道中名称完全匹配、且真实 S 怪在加载范围内的台词。不读取玩家聊天。相同台词去重保存。");
        if (ImGui.Button("加载／刷新已确认台词库")) Enqueue("dialogue-load");
        ImGui.TextUnformatted($"台词库：{dialogueLines.Length} 句，{dialogueLines.Select(l => l.NameId).Distinct().Count()} 只 S 怪");
        if (ImGui.TreeNode("47 只 S 怪台词核对清单"))
        {
            foreach (var profile in HuntDialogueCatalog.Profiles)
                ImGui.TextWrapped($"{profile.Name}：已核对 {profile.Rows.Length} 个原版台词 ID；当前可播放 {dialogueLines.Count(l => l.NameId == profile.NameId)} 句。"
                    + (profile.Rows.Length == 0 ? "暂无确认资料，等待实战收集。" : ""));
            ImGui.TreePop();
        }
        var actor = ControlActor(view);
        var entry = view.Entries.FirstOrDefault(e => e.Actor == actor);
        var hunt = entry is null ? null : catalog.FirstOrDefault(h => h.ModelId == entry.Applied.ModelId && h.Name == entry.ReplacementName && h.Rank == 3);
        ImGui.TextUnformatted($"台词对象：{hunt?.Name ?? "请先生成或替换 S 怪，并选为控制对象"}");
        if (hunt is not null && HuntDialogueCatalog.Profiles.FirstOrDefault(p => p.NameId == hunt.NameId) is { } profileSource
            && ImGui.SmallButton("打开此怪 Wiki 台词资料")) Dalamud.Utility.Util.OpenLink(profileSource.WikiUrl);
        var lines = dialogueLines.Where(l => l.NameId == hunt?.NameId).ToArray();
        if (lines.Length == 0) ImGui.TextWrapped("该怪暂无已确认台词。遇到真实怪物说话后会自动收集；未收录不代表它不会说话。");
        if (ImGui.BeginChild("huntDialogue", new Vector2(0, 180), true))
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                ImGui.PushID(i);
                ImGui.BeginDisabled(Loading || entry is null || entry.WaitingForDraw || entry.Restoring);
                if (ImGui.SmallButton("播放气泡"))
                    requests.Enqueue(new(Interlocked.Read(ref epoch), "dialogue-play", actor, DialogueNameId: line.NameId, Text: line.Text));
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextWrapped($"{(line.RowId == 0 ? "[实战收集]" : $"[NpcYell {line.RowId}]")} {line.Text}");
                ImGui.PopID();
            }
        ImGui.EndChild();
        ImGui.BeginDisabled(Loading || hunt?.NameId != HuntDialogueCatalog.Atticus || entry is null || entry.WaitingForDraw || entry.Restoring);
        if (ImGui.Button("阿提卡斯四句连续气泡 /hms talk")) Enqueue("dialogue-sequence", actor);
        ImGui.EndDisabled();
        if (ImGui.Button("停止台词并关闭演示气泡 /hms stoptalk")) Enqueue("dialogue-stop");
        ImGui.TextWrapped(view.DialogueRunning ? "台词序列播放中。" : "台词序列未播放。");
        ImGui.TextWrapped("气泡挂在模型骨骼上，随模型移动与缩放。仅本机显示，不发送聊天、不触发技能；动作暂停不暂停气泡计时。47 只 S 怪已逐页核对，内置 5 只、44 句不同台词；其余暂无确认资料，可继续实战收集。");
    }

    private void Rotate(ActorIdentity? actor, float degrees)
        => requests.Enqueue(new(Interlocked.Read(ref epoch), "rotate", actor, AngleDegrees: degrees));

    private void DrawModelControls(UiSnapshot view)
    {
        ImGui.SetNextItemWidth(270);
        var selectedActor = ControlActor(view);
        var selectedEntry = view.Entries.FirstOrDefault(e => e.Actor == selectedActor);
        var automatic = controlSelection is not { } explicitChoice || !view.Entries.Any(e => e.Actor == explicitChoice.Actor);
        if (ImGui.BeginCombo("控制对象", automatic ? "自动（优先本地预览）" : selectedEntry?.ReplacementName ?? "无演示对象"))
        {
            if (ImGui.Selectable("自动（优先本地预览）", automatic)) controlSelection = null;
            foreach (var demo in view.Entries)
                if (ImGui.Selectable($"{demo.ReplacementName}（{(demo.Actor == view.LocalActor ? "本地生成" : demo.OriginalName)}）##control{demo.Actor.Index}-{demo.Actor.ObjectId}",
                    !automatic && demo.Actor == selectedActor)) controlSelection = new(demo.Actor);
            ImGui.EndCombo();
        }
        var actor = ControlActor(view);
        var entry = view.Entries.FirstOrDefault(e => e.Actor == actor);
        var paused = actor.HasValue && (view.PausedActors?.Contains(actor.Value) ?? false);
        ImGui.BeginDisabled(Loading || entry is null || entry.WaitingForDraw || entry.Restoring);
        if (ImGui.Button("暂停动作 /hms pause")) Enqueue("pause", actor);
        ImGui.SameLine();
        if (ImGui.Button("恢复动作 /hms resume")) Enqueue("resume", actor);
        if (rotationInputActor != actor)
        {
            rotationInputActor = actor;
            rotationDegrees = view.Headings?.FirstOrDefault(h => h.Actor == actor)?.Degrees ?? 0;
        }
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("水平旋转（度）", ref rotationDegrees, 0, 360, "%.1f°")) Rotate(actor, rotationDegrees);
        var heading = view.Headings?.FirstOrDefault(h => h.Actor == actor)?.Degrees ?? rotationDegrees;
        if (ImGui.SmallButton("左转45°")) { rotationDegrees = (heading + 315) % 360; Rotate(actor, rotationDegrees); }
        ImGui.SameLine();
        if (ImGui.SmallButton("右转45°")) { rotationDegrees = (heading + 45) % 360; Rotate(actor, rotationDegrees); }
        ImGui.SameLine();
        if (ImGui.SmallButton("应用角度")) Rotate(actor, rotationDegrees);
        ImGui.SameLine();
        if (ImGui.SmallButton("解除固定朝向")) Enqueue("release-rotation", actor);
        ImGui.EndDisabled();
        if (entry is not null) ImGui.TextWrapped($"{entry.ReplacementName} · {(paused ? "已暂停" : "播放中")} · 朝向 {heading:F1}°。可在控制对象中选定模型，无需持续选中它。");
        if ((view.PausedActors?.Length ?? 0) > 0)
        {
            if (ImGui.SmallButton("恢复所有暂停动作")) Enqueue("resume-all");
            ImGui.SameLine();
            ImGui.TextUnformatted($"已暂停 {view.PausedActors!.Length} 个演示对象");
        }
        ImGui.TextWrapped("仅控制本机显示；真实小怪的服务器移动与伤害仍继续。手动旋转会停止自动连招。");
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
        if (view.ComboRunning)
        {
            if (ImGui.Button("停止布弗鲁连招 /hms stop")) Enqueue("stop-combo");
            ImGui.TextWrapped($"{view.ComboStatus}，本阶段剩余 {view.ComboRemaining:F1} 秒。");
        }
        if (view.ComboDirections.Length > 0)
        {
            ImGui.TextWrapped($"吸气方向记录（相对开始朝向）：{view.ComboDirections}");
            if (!view.ComboRunning) ImGui.TextWrapped(view.ComboStatus);
        }
        var playActor = ControlActor(view);
        var entry = view.Entries.FirstOrDefault(e => e.Actor == playActor);
        ImGui.TextUnformatted($"演示对象：{entry?.ReplacementName ?? view.TargetName}{(playActor == view.LocalActor && playActor.HasValue ? "（本地生成）" : "")}");
        var family = entry is not null && modelFamilies.TryGetValue(entry.Applied.ModelId, out var key) ? key : "";
        var reason = Loading ? "请等待角色进入场景。"
            : entry is null ? "请先生成本地预览，或替换当前目标的模型。"
            : entry.WaitingForDraw || entry.Restoring ? "请等待对象完成重绘。" : null;
        if (playActor.HasValue && (view.PausedActors?.Contains(playActor.Value) ?? false)) reason = "对象已暂停，请先恢复动作。";
        var hunt = entry is null ? null : catalog.FirstOrDefault(h => h.ModelId == entry.Applied.ModelId
            && h.Name == entry.ReplacementName);
        if (hunt is not null && HuntSkillPresets.Profile(hunt.NameId) is { } profile && profile.ModelId == entry!.Applied.ModelId)
        {
            ImGui.TextUnformatted($"{hunt.Name} · 专属技能（Wiki 收录 {profile.Skills.Length} 项）");
            if (ImGui.SmallButton("打开此怪的 Wiki 技能资料")) Dalamud.Utility.Util.OpenLink(profile.WikiUrl);
            ImGui.Checkbox("显示同名动作版本（需试播确认）", ref showSkillVariants);
            if (ImGui.BeginChild("huntSkillPresets", new Vector2(0, 200), true))
            {
                foreach (var item in profile.Skills)
                {
                    var skill = HuntSkillPresets.Resolve(item.Name, item.Primary, skills);
                    ImGui.PushID(item.Name);
                    ImGui.BeginDisabled(reason is not null || skill is null);
                    if (ImGui.Button(skill is null ? item.Name : $"{item.Name}  /hms play {skill.ActionId}"))
                        requests.Enqueue(new(Interlocked.Read(ref epoch), "play", playActor, SkillId: skill!.ActionId));
                    ImGui.EndDisabled();
                    if (skill is null)
                        ImGui.TextDisabled(item.Mode == "variants" ? "同名动作有多个版本，勾选上方选项后可试播。"
                            : item.Mode == "unavailable" ? "暂无可单独播放的本地释放动作。" : "本机技能表与资料不符，暂不播放。");
                    else if (item.Mode == "resource") ImGui.TextDisabled("按 Wiki 技能名与本机模型资源对应；动作 ID 为演示映射。");
                    if (item.Primary?.ActionName is { } actionName) ImGui.TextDisabled($"当前国服技能名：{actionName}");
                    if (showSkillVariants)
                        foreach (var mapping in item.Variants.Where(v => v.ActionId != skill?.ActionId))
                        {
                            var variant = HuntSkillPresets.Resolve(item.Name, mapping, skills);
                            ImGui.BeginDisabled(reason is not null || variant is null);
                            if (ImGui.SmallButton($"试播版本 {mapping.ActionId}##variant{mapping.ActionId}"))
                                requests.Enqueue(new(Interlocked.Read(ref epoch), "play", playActor, SkillId: mapping.ActionId));
                            ImGui.EndDisabled();
                        }
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
            ImGui.TextWrapped("专属技能名按此怪的 Wiki 战斗技能核对；同名版本单独列出。这里只演示单次动作，实际效果仍需游戏内确认。");
            if (hunt.NameId == HuntSkillPresets.BufruNameId)
            {
                ImGui.SetNextItemWidth(240);
                ImGui.SliderFloat("连招动作停留（秒）", ref sequenceHoldSeconds, 0.75f, 10f, "%.2f");
                ImGui.BeginDisabled(reason is not null || view.ComboRunning);
                if (ImGui.Button("五次吸气 → 五次喷嚏 /hms combo")) Enqueue("start-combo", playActor);
                ImGui.EndDisabled();
                ImGui.TextWrapped("连招记录五次随机方向，吸气后按同顺序转向喷嚏。咏唱以面板倒计时表示；可调整动作停留避免打断。停止后取消剩余步骤，当前动画可能继续至结束。仅本机演示，不产生真实范围伤害、击退或击倒。");
            }
            ImGui.Separator();
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
        chat.ChatMessage -= OnNpcDialogue;
        requests.Clear();
        nameplatePresentation.Dispose();
        try
        {
            void Shutdown()
            {
                sequence.Stop("插件卸载，连招已取消。");
                modelRotation.Clear();
                try { dialoguePreview.Reset(); }
                catch (Exception ex) { log.Error(ex, "卸载时关闭演示气泡失败"); }
                try { animationPause.Reset(); }
                finally
                {
                    pauseAccess.Dispose();
                    try { modelVisibility.Reset(); }
                    finally
                    {
                        try { localPreview.Clear(); }
                        finally { session.Shutdown(); }
                    }
                }
            }
            if (framework.IsInFrameworkUpdateThread) Shutdown();
            else framework.RunOnFrameworkThread(Shutdown).GetAwaiter().GetResult();
        }
        catch (Exception ex) { log.Error(ex, "卸载时恢复外观失败"); }
    }
}

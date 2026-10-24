# Glamourer 与普通怪物换模

检查日期：2026-10-25。Glamourer 当前默认分支为 `testing`，本次核对的提交为 `e165910345b17b99d9aa35990005b5e0f73ae65b`。

## 结论

现成的 Glamourer IPC 不能承担本插件的「普通战斗小怪 → 指定 S/A/B 怪物」功能。

入口 `StateEditor.ChangeModelId` 会调用 `InternalStateEditor.ChangeModelId`。后者先拒绝所有非零模型 ID，然后拒绝现有非人形对象：

```csharp
if (modelId is not 0)
    return false;
// ...
if (!oldIsHuman)
    return false;
```

源文件：[InternalStateEditor.cs](https://github.com/Ottermandias/Glamourer/blob/e165910345b17b99d9aa35990005b5e0f73ae65b/Glamourer/State/InternalStateEditor.cs)。

虽然存在 `LoadNonHuman`、模型编号字段以及重绘代码，这些结构的存在不能证明对外支持给普通怪物换成另一种怪物。`StateApi.ApplyState` 最终仍走设计应用与相同模型变更检查。仅解除上述检查，也不能证明其余状态合并、NPC 身份分组、恢复与模型创建分支支持该用法。

可参考的是「原始状态/当前状态分离、外观数据快照、统一重绘、恢复」的思路。Glamourer 的重绘通过 Penumbra 完成；本插件没有复制其状态管理框架，也不调用其 IPC。

## 独立插件采用的路线

1. 从本机 `TerritoryType → NotoriousMonsterTerritory → NotoriousMonster` 取得各地图常规狩猎名单。后续 SS/随从条目的 Rank 也可能为 S/B，所以不能只按 Rank 筛选整张表。
2. `NotoriousMonster` 同时引用 `BNpcName` 和 `BNpcBase`，因此不需要从外部名称字典猜测基础 NPC。名称表单独以客户端语言加载，避免无语言表的 RowRef 导致本地化读取错误。
3. 从 `BNpcBase` 读取 `ModelChara`、`BNpcCustomize`、`NpcEquip` 和 Scale。不要把名称 ID、基础 NPC ID 和模型 ID 当成同一个编号。
4. 对当前目标捕获完整的本插件所写外观字段；在游戏框架线程写入目标外观，隐藏模型，在下一帧重新启用绘制。对原始身份、HP 和位置字段不执行写入。
5. 每次读取和写入前，从对象表重新解析对象并比较完整身份；切图取消队列与旧快照，卸载恢复仍存在的对象。

## 0.1.0.1 名称、等级与真实体型

- 名称只修改 `GameObject.Name` 的 64 字节 UTF-8 显示缓冲区，设置 `ObjectUpdateFlags.Name` 并通过 Dalamud `INamePlateGui.RequestRedraw` 请求刷新；数值等级写入 `CharacterData.Level`。`NameId`、`BaseId` 与对象 ID 保留，因此显示名称变化不会破坏本插件的身份核验，也不会把普通小怪变成 HHT 中的真实狩猎记录。
- `NotoriousMonster`、`BNpcBase` 没有数值等级字段，本版显式使用常规狩猎的已知版本等级：2.0～7.0 分别为 50/60/70/80/90/100。未映射的未来版本不猜测等级，应用时提示。
- 目标原版大小以所选怪物 `BNpcBase.Scale` 为 1.00 倍，同步设置模型缩放、对象缩放与绘制对象缩放，避免沿用原小怪的体型。选择其他怪物时重置为 1.00 倍。离线检查可确认读写值符合该基准；最终画面尺寸仍需游戏内对照原怪验证。
- 快照保存完整原名称缓冲区、等级、模型缩放、对象缩放和原绘制向量，恢复与卸载时一同还原。每帧重新核验对象身份后，只维持名称/等级/缩放，不反复重绘模型。

该路线仍需游戏内验证。骨架文件存在、原生结构的读写检查通过，都不能保证任意两个怪物的战斗动画兼容。

## 0.1.0.2 本地技能动作演示

调用本机国服 API 15 的 `Character.Timeline.TimelineSequencer.PlayTimeline(ushort, void*)`，只请求一次本地动画调度，不调用 `ActionManager.UseAction`，不合成战斗消息。技能 Action ID 通过 `Action.AnimationEnd` 转换为时间轴 ID，二者不能混用；不直接播放受击对象的 `ActionTimelineHit`。当前不模拟读条阶段和方向序列，也不额外加载独立施法/命中特效。

本机可读取 47,121 条具有有效动作时间轴的非玩家技能候选记录，其中存在同名不同 ID、共用时间轴和历史内容。`mon_sp/[SKL_ID]/...` 是按目标骨架解析的模板，并不表示任意模型都拥有对应技能。不能仅根据同名、相同时间轴或同骨架推导怪物真实技能归属。

第一组人工核对预置为布弗鲁（BNpcName 10617、ModelChara 3315、模型资源 m0741）：

| 技能 | Action ID | AnimationEnd |
| --- | --- | --- |
| 巨魔碎颈臂 | 27316 | 3205 |
| 五倍吸气 | 27307 | 3201 |
| 五倍喷嚏 | 27309 | 3202 |
| 上击 | 27314 | 3204 |
| 腐烂孢子 | 27313 | 3203 |

技能名称及归属依据用户提供的[灰机 Wiki 布弗鲁页面摘录](https://ff14.huijiwiki.com/wiki/布弗鲁)；本次直接访问页面受安全验证限制，没有把页面本身标为已读取。五个 ID 已与本地国服表对应，`chara/action/mon_sp/m0741/mon_sp001..005.tmb` 均存在。这只证明资源映射，不证明运行时画面、特效或战斗机制正确。

播放由 UI/命令加入框架线程队列，执行时重新核验对象身份，要求对象仍在替换会话中、未重绘/恢复、存活、模型 ID 一致且绘制对象加载完成。连续播放间隔 750 ms。技能表首次显式请求时才加载；不注册原生界面事件回调。

离线检查覆盖命令拒绝非法参数、槽位复用、重绘/恢复期间拒绝播放、节流、Action ID 到时间轴映射，以及布弗鲁的模型/技能文件。实际动画和附带特效仍需游戏内验证。

## 0.1.0.2 隐藏、头顶名称与名牌标记

- 隐藏只设置 `GameObject.RenderFlags` 的 `VisibilityFlags.Model` 位，不调用移除对象接口，不覆盖名牌位和其他渲染位。分别筛选本地玩家以及无玩家归属的敌对战斗 NPC，已替换对象和本插件创建的本地预览被排除。记录原模型位，关闭/卸载时只恢复这一位，原本已隐藏的对象不强制显示。
- 每次操作重新解析完整对象身份；对象消失即丢弃记录。切图/登出尝试恢复当前仍匹配的对象，然后清理旧场景记录并关闭开关。`/hms show` 只恢复本插件控制的显示状态；`/hms restore` 另包含恢复替换和移除本地生成对象。
- 普通怪或 A 怪的头顶名称可能在 UI 层再次按 `BNpcName` 解析，单改 `GameObject.Name` 无法覆盖该路径。通过 `INamePlateGui.OnNamePlateUpdate` 提供的 handler 对完整身份匹配的对象调用 `SetField(NamePlateStringField.Name, ...)`，恢复过程中使用原名。事件在 Dispose 中解绑，不注册自己的原生 UI 委托。
- `CharacterData.Icon` 是名牌图标类别，外观快照新增这一字段，替换时取目标 `BNpcBase.Rank`，每次刷新维护，恢复时还原。本机所有 201 种常规 S/A/B 数据的 Rank 都为 1；这个类别不是本插件显示的 S/A/B 分级，也不是任务/攻击目标标记 ID。实际小图标最终样式需游戏内验证。

## 0.1.0.2 本地生成预览

使用国服程序集已有的 `ClientObjectManager.CreateBattleCharacter`、`GetObjectByIndex`、`DeleteObjectByIndex` 和 `CharacterSetupContainer.SetupBNpc`。只接受本地池分配的索引，并核验对象表索引位于 200～448 的非网络对象范围。按角色朝向的正前方 2～30 米放置、朝向角色，不做地面碰撞探测。未复制 Brio 的实现代码。

通过 BNpcBase/BNpcName 设置外观后，将预览对象真实 `BaseId` 和 `NameId` 置零，保持中文显示名称与所选等级，避免生成对象伪装为真实狩猎记录。只保留一个本地对象。模型名称的 C-string 终止符后存储 64 位随机所有权标记；删除前同时核验管理器地址、池索引、原生指针、完整对象身份和标记，拒绝删除已复用的槽位。名称覆盖和再次换模保留此标记。

本地生成会话与换模会话分开管理；清理本地对象并撤销其换模记录，不对已销毁的对象重绘。部分初始化失败会尝试删除刚分配的对象；删除失败保留所有权以供重试。切图清理已失效的会话时不按旧索引盲目删除。生成对象和演示动作只影响本机。

当前共 297 项离线检查通过，含隐藏/恢复、所有权丢失、槽位复用、部分失败清理、位置计算、名牌原名和类别恢复，以及 201 种中文名称的所有权标记空间。没有将编译/离线结果视为游戏内生成、动画或名牌效果已经通过。

## 0.1.0.3 原版大小与本地预览名牌

默认关闭「手动调整体型」，替换和生成均传入目标原版倍率 1，始终使用所选 `BNpcBase.Scale`，不按原小怪缩放折算。选择其他怪物时关闭自定义并重置倍率，界面直接显示目标原版大小。

本地生成对象在 `INamePlateGui.OnDataUpdate` 中仅清除 `handler.VisibilityFlags` 的 `0x2` 血条位，保留 `0x1` 名字文字及其他位。该事件覆盖每次名牌数据更新，不依赖只有重要变更才触发的名称事件，也不每帧请求全局重绘。先按对象 ID 筛选，再验证完整对象身份和原生所有权标记；不遍历解析所有名牌对象。真实怪物、已移除的预览和所有权丢失的复用槽位不应用此修改。

中文名字继续通过原有名称更新事件设置；等级与名牌类别继续由目标外观维护，保留游戏的等级前缀与图标绘制。没有把 HP 设为 0，没有删除 UI 节点，也没有修改游戏的全局名牌配置。名字/等级是否显示仍遵循游戏设置。血条位含义参考 [FFXIVClientStructs NamePlateNumberArray](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Arrays/NamePlateNumberArray.cs)，事件及属性参考 [Dalamud INamePlateGui](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Plugin/Services/INamePlateGui.cs) 与 [NamePlateUpdateHandler](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/Gui/NamePlate/NamePlateUpdateHandler.cs)。

302 项离线检查通过，新增检查覆盖血条位、文字/其他位保留、真实对象隔离、移除后停止覆盖，以及同身份但所有权标记丢失的槽位保护。未将离线结果视为游戏内名牌画面已验证。

## 0.1.0.4 指定对象隐藏与自由体型

替代批量隐藏：`ModelVisibility` 维护按完整身份选中的对象集合，`Hide` 只登记指定对象，`Show` 只撤销一项，`Reset` 撤销全部并恢复原显示位。已隐藏和待重试对象提供 UI 恢复列表，包含名称与索引，避免隐藏后难以重新选中。保留独立隐藏自己；不再逐帧扫描所有怪物，只重新解析选中的对象与本地玩家。

隐藏入口核验敌对、战斗 NPC、无玩家归属；玩家、宠物、友方 NPC 被排除。对象离开后丢弃完整身份记录。换模等待期间不捕获临时隐藏位，也不提前恢复；重绘结束后再应用指定隐藏。名称相同的对象独立管理，已替换外观的真实敌对怪也可以被明确选中隐藏。

体型输入取消独立开关与 0.1～3 的相对滑块，选择怪物时直接填入目标 `BNpcBase.Scale`。输入值即模型、对象与绘制向量所用的绝对缩放值，不再额外乘目标原版值。`ModelSize` 仅接受大于 0 的有限值。生成前先校验，非法倍率不会删除已有预览。

`SwapSession.Resize` 只对仍匹配身份、已完成重绘且未恢复中的演示对象更新 `Applied` 的三个缩放字段，并维护新值；保留原始快照和其余外观。UI 提供「应用体型到演示对象」，优先本地预览，否则当前被替换目标，无需销毁/重建或重绘模型。

321 项离线检查通过，新增指定隐藏、多对象恢复、重绘等待、槽位复用与失败重试，以及绝对倍率、大/小正数、非法数值、原外观恢复和过期身份拒绝。最终显示效果仍需游戏内验证。

## 0.1.0.5 本地预览敌对名牌

用户截图中真实小怪换模后的左侧名牌使用黄色敌对怪样式，右侧本地生成对象虽然名称/等级正确，却使用绿色友方样式并缺少精英标记。生成代码只设置战斗 NPC 类型、外观与图标类别，遗漏敌对显示关系。

`BNpcBase.Battalion` 是阵营数据，角色的 `Battalion` 参与友敌关系判断，`CharacterData.IsHostile` 是独立标志。本机 201 种常规狩猎外观均读取到阵营 4。对本机客户端 `GetNamePlateColorType` 的静态核对也确认其战斗 NPC 分支先检查友敌关系，再选择 NPC/未交战敌人等颜色类别。不能只把绿色文字改成固定黄色。

新增 `NativeLocalPresentationCodec`，在本地分配且经过范围验证的生成对象上设置目标阵营与敌对位，名称、等级、精英图标继续由游戏原生名牌路径处理。框架更新仅对完整身份和所有权 nonce 仍匹配的租约维护这些字段；只有字段变化时才标记名称更新和请求名牌刷新。保留 HP、真实名称/基础 ID 为 0、其他角色标志和外观字段；不应用于真实小怪。原有头顶血条屏蔽继续生效。

330 项离线检查通过，新增阵营数据、敌对位及其他位保留、原生结构字段隔离、无变化不刷新、客户端刷新后重新维护、丢失所有权后不写入的回归检查。没有在运行中的游戏里调用测试，也未将此结果视为截图样式已经通过实测。更新后需移除旧本地预览并重新生成。

参考：[CharacterData](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Character/CharacterData.cs)、[GameObject.GetNamePlateColorType](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Object/GameObject.cs)、[BNpcBase 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/BNpcBase.yml)、[Battalion 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/Battalion.yml)。

## 0.1.0.6 布弗鲁方向连招

按用户提供的灰机 Wiki 摘录，先记录五次随机前/右/后/左吸气方向，再按相同顺序喷嚏。直接访问该页面仍返回 403，本版使用用户给出的摘录作为技能归属资料。动作和时长从本机国服数据核对：27307/27308 吸气均为时间轴 3201（m0741/mon_sp001），咏唱分别 4000/300 ms；27309/27310 喷嚏均为 3202（mon_sp002），咏唱 5000/300 ms。

`SkillAnimation` 新增本机表的 `CastTimeMs`。`BufruSequence` 在框架线程逐段计时，调用既有 `ActionPreview.Play` 与原生 `GameObject.SetRotation`，不调用游戏施法接口。方向相对于初始朝向，数组在开始时复制；每个步骤只有咏唱等待和动作停留两个阶段。停留默认 2000 ms，可调 750～10000 ms。卡顿后只推进一个阶段，并以当前帧重新定时，避免集中补播。

对象必须仍处于布弗鲁外观会话、没有重绘/恢复，并通过原生模型、存活、绘制状态核验。本地预览另核验 nonce；取消后的旧对象不得收到朝向恢复。正常结束和显式停止先清除运行状态，再尝试恢复初始朝向。场景清理仅丢弃状态，不使用旧索引。动画接口失败会停止一次并记录错误，不继续每帧重试。

UI 提供开始/停止、记录方向、阶段剩余时间，命令为 `/hms combo` 和 `/hms stop`。当前咏唱只是插件倒计时，不启动原生咏唱状态或 Action.AnimationStart（对应记录不是直接可用的完整战斗咏唱实现）。停止不保证切断当前已经调度的动画；独立 VFX、地面 AoE、命中、击退、击倒与其他状态未模拟。

357 项离线检查通过，新增十次方向/动作顺序、短重复咏唱、本地所有权丢失、场景变化、停止恢复、接口失败、卡顿不突发补播和非法输入。演示的最终转向、动作持续时间和附带特效仍需游戏内测试。

## 0.1.0.7 全部常规 S 怪技能资料

2026-10-25 已通过普通浏览器逐页读取本机名单内全部 47 只常规 S 怪的灰机 Wiki「战斗技能／技能／战斗方式」章节。网页工具直连曾返回 403，但浏览器可正常阅读；本版完成了实际页面核对。仅整理游戏技能名称等事实，不将原文攻略或完整章节装入源码与发布包。每个资料条目保留原页面 URL。

`SRankSkillProfiles.json` 收录 300 项。138 项的 Action ID 与固定版本的公开 BossMod 狩猎战斗记录对应，116 项按 Wiki 技能名与本机模型唯一兼容资源映射；后者不能据此宣称是该怪的真实 Action ID。34 项有多个同名动作资源，独立列为需试播的版本；12 项没有可单独播放的释放动作，禁用且说明原因。各怪至少有一个直接可用的动画映射。通用骨架和魔法时间轴只有实际文件存在才保留，但文件存在并不能证明实际演示效果。

多智兽「迅速回旋」对应当前国服「迅猛回旋」，顾尼图「轮转枪」对应「转轮枪」，阿格里帕「等离子立场」对应「等离子力场」。资料保留 Wiki 标签，映射单独保存当前国服名称；运行时严格校验名称、时间轴 ID 和路径，客户端表变动时禁用按钮。资料按 NameID 和模型绑定，不会将布弗鲁连招应用到其他 S 怪。

生成脚本只读取本地研究输入并输出事实映射；研究缓存、完整 Wiki 文本、上游克隆不随源码包分发。固定来源与许可见 `THIRD_PARTY_NOTICES.md`。Release 编译无警告，1459 项离线检查通过，涵盖所有条目和本机资源；游戏内最终动画、特效及各同名版本仍待验证。其他 S 怪本版提供单次动作，自动转向连招仍仅布弗鲁支持。

## 0.1.0.8 预览名牌高度与被动小怪隐藏

旧实现仅维护 ModelScale、GameObject.Scale 与 DrawObject.Object.Scale，未更新原生名牌的 NameplateOffset/NameplateOffsetTarget 缓存。对本机客户端文件的只读检查显示，名牌位置使用已缩放的挂点偏移，加上 NameplateOffsetScaleMultiplier × GameObject.Scale。原生挂点优先 45，缺失时回退 46；字段及虚函数按本机 FFXIVClientStructs 7.57.0.9602 反射核验，上游定义见 [GameObject](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Object/GameObject.cs) 和 [DrawObject](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Graphics/Scene/DrawObject.cs)。没有将分析中的地址或偏移写成插件硬编码。

NativeLocalActorAccess 在完整 lease/nonce 通过且模型 LoadState=3 后，从 DrawObject.GetAttachBoneLocalLocation 取未缩放挂点，结合实际绘制缩放、DrawOffset 与 GameObject.GetHeight 更新两个缓存；未加载时等待，无挂点时使用原生高度/偏移回退。原生额外文字间距由游戏继续处理，避免重复计入。NativeLocalNameplateCodec 只处理有限数值，绝不从上一帧的已缩放缓存继续相乘。绘制缩放确实变化时设置 IsTransformChanged，让渲染变换重算；相同缩放不反复标脏。高度修正仅通过所有权检查后的本地预览路径调用，不写真实 NPC 的名牌偏移。

旧 CanHide 检查 IsHostile，使部分被动战斗 NPC 被拦截。改为核验完整目标身份、战斗 NPC 类型/Combatant 子类型及无玩家 Owner，不再将主动攻击状态作为隐藏条件。本机 BNpcName 395/1142 为蜜蜂云，401 为跳蜥幼体；未使用名称白名单，其他符合条件的被动小怪同样可隐藏。没有修改敌对、交战或身份字段。

Release 编译无警告，1480 项离线检查通过；新增缩放后名牌位置、放大后缩小、换模型、重复帧不累计、非法坐标拒绝、未加载等待、绘制变换标脏与被动 NPC 允许隐藏/宠物拒绝等回归检查。运行时挂点读取、显示时机与两种小怪的实际隐藏效果仍需游戏内确认。

## 主要参考

- [Glamourer 模型编辑入口](https://github.com/Ottermandias/Glamourer/blob/e165910345b17b99d9aa35990005b5e0f73ae65b/Glamourer/State/StateEditor.cs)
- [Glamourer 状态 IPC](https://github.com/Ottermandias/Glamourer/blob/e165910345b17b99d9aa35990005b5e0f73ae65b/Glamourer/Api/StateApi.cs)
- [FFXIVClientStructs ModelContainer](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Character/ModelContainer.cs)
- [NotoriousMonster 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/NotoriousMonster.yml)
- [BNpcBase 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/BNpcBase.yml)
- [MonsterModelSwap 换模服务](https://github.com/Jayty07/MonsterModelSwap/blob/main/MonsterModelSwap/Services/ModelSwapService.cs)（原实现针对玩家，不能直接当作普通 NPC 换模成品）。
- [FFXIVClientStructs 动作调度器](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Control/ActionTimelineSequencer.cs)

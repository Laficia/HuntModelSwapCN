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

## 0.1.0.9 批量隐藏与逐只隐藏并存

ModelVisibility 增加独立 HideAllMonsters 状态。仅开关启用时枚举对象表，按与逐只隐藏相同的战斗 NPC/无玩家 Owner 条件生成候选，排除本地 lease 和非恢复中的替换会话。实际写入前仍按完整身份重新解析；不使用名称或模型号白名单。

批量候选与显式逐只隐藏集合取并集，自己的开关独立。Show 在批量模式添加逐对象显示例外，Hide 取消对应例外；关闭再开启批量模式会清空例外。停止批量模式后继续保留显式隐藏集合，离开候选但仍存在的对象按原始位恢复。重绘期间推迟记录或恢复；Reset 和场景清理同时清空批量状态、例外与原有选择，保留既有恢复失败重试机制。

Release 编译无警告，1494 项离线检查通过。新增批量新入范围、逐只恢复不再隐藏、关闭批量保留逐只状态、重新开启清除例外、对象变为演示对象后恢复、对象槽复用、重绘等待以及全部恢复/场景清理检查。最终游戏内批量隐藏效果仍需确认。

## 0.1.0.10 动画暂停与级别全选

本机 FFXIVClientStructs 7.57.0.9602 的 TimelineContainer.OverallSpeed（0x2c8）、TimelineSequencer.TimelineSpeeds（14 槽）及 OwnerObject/Parent 关系已通过反射核验。上游接口说明见 [TimelineContainer](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Character/TimelineContainer.cs) 与 [ActionTimelineSequencer](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Control/ActionTimelineSequencer.cs)。只读扫描本机客户端文件确认 CalculateAndApplyOverallSpeed 和 SetSlotSpeed 的安装版特征码各匹配一次；插件使用 FFCS 已解析地址，不硬编码分析地址。

AnimationPause 保存完整对象身份、预期模型与原有整体/各槽速度。所有权、会话和模型校验通过后，将整体与各槽速度设为零。NativeAnimationPause 在第一次明确暂停请求时创建两个速度接口钩子，针对已暂停且仍有效的对象保持零速；其他对象原样调用原函数。两个回调委托以实例 readonly 字段强引用保留至钩子释放，边界校验异常放行原生路径并仅记录一次。初始化或打开窗口不创建原生钩子。

恢复先移除拦截标记再写回捕获的各槽速度，避免恢复值被自身钩子改为零；重复暂停不覆盖初始快照。部分失败保留恢复快照；冻结失败不每帧反复重试，恢复失败可再次操作。换模、移除和恢复前释放对应暂停，卸载释放全部并销毁钩子；对象消失、模型或 nonce 变更不向旧槽写回。调整体型不解除暂停。BufruSequence 在暂停时保存阶段计时位置，跳过转向和动作调度，恢复时平移截止时间，保留余下时长。

狩猎级别使用既有 rankFilter=0 全部条件，默认改为全部并加直接的全选按钮，保留版本与名称筛选。Release 编译无警告，1522 项离线检查通过，新增所有槽速度恢复、重复暂停、部分失败、多个对象、所有权/槽位失效及连招咏唱/停留暂停等回归检查。离线特征码匹配不等于游戏运行验证；本版原生钩子的暂停效果和物理/特效限制仍需游戏内确认。不会停止服务器移动、战斗伤害或全局游戏时间。

## 0.1.0.11 恢复修复、旋转与地图选择

用户反馈恢复动作无效后，只读检查本机 SetSlotSpeed 指令确认：它先比较 TimelineSpeeds 缓存，值相同则直接返回，改变时才写入缓存并标记调度器更新。旧 NativeAnimationClockCodec.Write 直接预填数组后再调用该接口，使后者跳过更新；旧离线内存回读检查没有覆盖原生调度器标脏行为。

修复为只先恢复 OverallSpeed，再逐槽通过原生 SetSlotSpeed 写入。若当前缓存恰好等于目标速度，先通过接口切到临时值，再写回目标值，确保旧版留下的已恢复缓存／仍冻结的渲染时钟也收到刷新；中间值不会作为最终速度保留。新的回归检查按本机观察到的同值跳过行为模拟调度器，检查全部 14 槽均被标脏和最终值正确，不再仅检查数组回读。恢复还为该对象登记一次原生整体速度传播刷新，避免整体值相同导致调用者跳过应用；消费前核验完整身份、模型和 nonce，过期记录在框架更新丢弃。显式恢复在无暂停快照时也能修复零速，原有快照恢复仍保留每槽原值。

主窗口顶部及可独立显示的悬浮窗共享控制选择，通过不可变选择引用绑定完整对象身份，避免本地预览总是覆盖用户指定的其他演示对象。/hms pause、resume、rotate 使用同一控制选择。ModelRotation 在会话、模型和 nonce 核验后调用既有 SetRotation 路径，每帧保持水平朝向；释放、对象失效和场景变化后停止写入。手动旋转停止自动连招，启动连招释放手动方向。暂停与旋转相互独立；没有操作网络位置或施法。提供 0～360° 滑条和 45° 步进，不实现俯仰/翻滚。

地图从 HuntAppearance 按 TerritoryId 去重，候选跟随版本；选项在全部版本时显示版本号。地图按 ID 精确匹配，与级别、版本和文本搜索取交集。新版本不含旧地图时自动回到全部地图。地图选择不更改场景、演示对象或执行传送。

Release 编译无警告，1536 项离线检查通过，新增原生标脏回归、重复恢复、零速修复、旋转角度归一化、客户端朝向刷新维持、释放、场景/所有权失效与非法角度检查。最终动画恢复、悬浮窗操作和模型旋转仍需游戏内验证。

## 主要参考

- [Glamourer 模型编辑入口](https://github.com/Ottermandias/Glamourer/blob/e165910345b17b99d9aa35990005b5e0f73ae65b/Glamourer/State/StateEditor.cs)
- [Glamourer 状态 IPC](https://github.com/Ottermandias/Glamourer/blob/e165910345b17b99d9aa35990005b5e0f73ae65b/Glamourer/Api/StateApi.cs)
- [FFXIVClientStructs ModelContainer](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Character/ModelContainer.cs)
- [NotoriousMonster 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/NotoriousMonster.yml)
- [BNpcBase 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/BNpcBase.yml)
- [MonsterModelSwap 换模服务](https://github.com/Jayty07/MonsterModelSwap/blob/main/MonsterModelSwap/Services/ModelSwapService.cs)（原实现针对玩家，不能直接当作普通 NPC 换模成品）。
- [FFXIVClientStructs 动作调度器](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Control/ActionTimelineSequencer.cs)


## 0.1.0.12 S 怪台词与气泡

2026-10-25 正常浏览器逐页读取 47 只常规 S 怪页面，展开「战斗对话」折叠栏后核对原版 YE 行号及简中台词。5 只页面明确提供战斗台词，42 只暂未在这些页面确认。结合固定提交 BossMod 的阿提卡斯 30 个方向喊话 ID，形成 61 条映射，与已安装国服 NpcYell 的文本逐条匹配，去重后 44 句。SRankDialogueProfiles.json 保留每只 S 的核对状态、来源 URL、模型及名称身份；不推断相邻行号的怪物归属。NpcYell.Name 在已检查记录中为 0，无法从该列直接关联 BNpcName。

HuntDialogueCatalog 对文本长度、控制字符、已知 S 怪归属和重复记录进行限制，客户端行号文本变化则跳过该条。IChatGui.ChatMessage 使用当前 API 15 的 IChatMessage，仅读 OriginalSender/OriginalMessage 的 ExtractText 副本；只接受 NPCDialogue / NPCDialogueAnnouncements。事件回调不保留消息对象、不访问原生对象，送入有界队列；框架线程检查 epoch、真实 S 怪 NameId 在当前对象表中存在，再归档并保存配置。生成对象 NameId=0，不作为真实来源。只收集新收到的频道台词，不解析历史或网络包。

NativeActorAccess 使用已安装 FFXIVClientStructs NpcYellBalloon.OpenBalloon/CloseBalloon，首次用户点击才调用，无新增钩子或原生回调。离线只读扫描当前国服 exe 文件，两者的已安装签名各唯一匹配，不硬编码地址。调用前完整对象身份、加载状态、会话模型、展示名称及本地 lease nonce 校验；校验 YellBalloon.Character 回指，使用默认骨骼回退，原生定时有限生命周期，printToLog=false。关闭前检查当前模型、回指、活跃状态及文字仍与本插件上次播放相同，不关闭后续不同文本的服务器气泡。

DialoguePreview 按框架时钟调度，长帧每次最多一步，不补播风暴。阿提卡斯四句按用户 0/6/9/13 秒间隔，动作暂停不冻结台词。换模或移除前清理，切图只遗忘旧会话，不解引用旧对象；停止按钮及卸载清理有限时气泡。只播放气泡，不自动释放技能或同步实战台词到演示模型。

Release 编译无警告，1828 项离线检查通过，包括全部 61 条客户端原文匹配、改名数据拒绝、47 只 S 怪收集归属、去重与容量、换图/换模/nonce 失效、四句时序与延迟帧。原生气泡实际位置、骨骼缩放与聊天来源仍需游戏内验证。未声称对 47 只怪进行实际战斗测试。

原生接口参考：https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/NpcYellBalloon.cs


## 0.1.0.13 生成 B 怪的蓝色被动精英标记

用户提供第十三个孩子的原版蓝色标记与本地生成红黄色标记对照。排查确认 NativeLocalPresentationCodec 在创建及每帧维护时统一设置 IsHostile=true，混淆了敌对阵营和主动攻击分类。BNpcBase.Rank=1 表示当前狩猎怪共有的精英图形类别，不决定红/蓝；狩猎 NotoriousMonster.Rank 才区分 B/A/S。

根据官方 2.35 更新说明 B 级精英由主动改为被动，以及官方 UI 指南蓝色表示被动、红色表示主动的说明，HuntAppearance 增加常规狩猎显示规则 IsAggressive：B=false，A/S=true。仅本地生成对象将该值写入 IsHostile；Battalion 仍保持目标阵营 4，保留精英图形、黄色敌对名牌文字、HP 隐藏和本地身份。只读检查当前国服 GetNamePlateColorType 指令，确认敌对颜色路径按 Battalion 分类，未将 IsHostile 位当成友方/敌方开关。该观察不等于渲染实测。

LocalActorLease 保存该显示分类，LocalPreview.Tick 按会话当前模型/名称重新解析；因此同一生成对象 B→S→B，即使模型编号相同也更新分类，不沿用最初生成外观。回指、索引、完整身份及 nonce 所有权检查保持。不会修改真实小怪的 IsHostile 或战斗行为。

Release 编译无警告，1837 项离线检查通过。新增被动/主动切换、标记更新无变化不脏、保留其他标志位和敌对阵营、对象换外观和删除、77 个 B/124 个 A+S 分类及第十三个孩子专项检查。原版蓝色图标的实际渲染仍需游戏内确认。

参考：
- https://na.finalfantasyxiv.com/uiguide/battle/battle-np/battle_np_icon.html
- https://na.finalfantasyxiv.com/lodestone/topics/detail/8bd64bf47da9032ed17891b220e0c3564e228182


## 0.1.0.14 真实小怪替换的原版头标

用户要求真实小怪被替换时也使用所选 B/A/S 的原版头标，原先只修复了生成对象。保持真实 IsHostile / Battalion 不变，改用 INamePlateUpdateHandler.NameIconId 覆盖显示资源。按完整 ActorIdentity 找当前有效 SwapEntry，再核对目标名称、模型及图形类别，B 被动蓝色、A/S 主动；等待重绘、恢复中和未知类别不覆盖。原有每次替换及恢复的 RequestRedraw 会让游戏刷新原版图标；不保存旧 UI 指针或槽位。只在 OnNamePlateUpdate 对有重要更新的名牌赋值，不每帧请求全局刷新；本地对象另核验 lease nonce。

只读检查本机 exe 的原生名牌图标选择分支，Character.Icon 类别 1 对应被动 0xf108=61704、主动 0xf10e=61710。另一处名牌更新分支具有相同选择并写入名牌数字数组。已从本机 ui/icon/061000/061704.tex 与 061710.tex 提取并按 A8R8G8B8（小端 BGRA）解码查看，确认前者蓝色被动精英、后者红黄色主动精英。仅将资源 ID 作为已知游戏数据使用，没有硬编码原生函数 RVA，也没有安装新钩子。其他当前狩猎类别均为 1；未来新类别保留游戏结果，等待核对。

Release 编译无警告，2048 项离线检查通过。新增全部 201 种外观的对应图标、两枚图标资源存在、主动来源换 B / 被动来源换 A / 换 S、连续替换、恢复/重绘/未跟踪不覆盖、名称模型类别不匹配拒绝、未知形状拒绝，以及真实怪物主动标志、战斗位、阵营、HP 和身份不被外观写入改变的检查。实际名牌渲染及恢复效果仍需游戏内验证。

接口参考：https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/Gui/NamePlate/NamePlateUpdateHandler.cs


## 0.1.0.15 六种特殊狩猎及 A/B 对应技能库

从 NotoriousMonsterTerritory 的首五项读取普通狩猎后，追加严格名称 ID 白名单：8915/8916、10615/10616、13406/13407。跨地图与四个重复随从槽按 NameId/BaseId 去重并合并 TerritoryIds，得到 207 种外观。三组 Boss 分为 SS，三种随从分为 SS随从；真实表 Rank 仍为 3/1，保持其原始数据，显示筛选用 FilterRank 独立表达。随从主动分类不按普通 B 被动规则套用。特殊怪保留各版本全部六张地图关系，地图下拉从普通地图生成，筛选及搜索会匹配特殊怪的实际地图集合。使用既有创建/替换、身份清零、lease nonce 和恢复路径，不生成真实狩猎身份。

正常浏览器逐页读取新增 160 种怪的 Wiki 技能章节，旧页面的「技能」标题单独处理；Cloudflare 自动验证和未加载页面重读后才计入。原始全文仅在 dist/research 私有缓存中，发布资源只保留名称事实、URL 及动作映射。新增 502 项：208 记录映射、224 资源映射、66 同名候选、4 未匹配；加原 S47 的 300 项，共 802 项（346/340/100/16）。前后左右葬、三种敕令、左右锤击按已核对的独立名称展开；不把条件说明或逆反效果描述当作新技能。流言石化之眼对应当前国服石化眼 18041，密涅瓦装填环形导弹对应装填环状导弹，运行时仍严格校验中文名称与时间轴。

只允许有固定 BossMod 记录佐证的 NPC Action ID 使用 mon_sp/gimmick/ 准备／机制时间轴；按名称筛选的普通资源候选不纳入这类通用资源。准备时间轴可能只有特效或没有明显动作，不自动释放后续技能。全能机甲与阿尔宾使用模型类型 1，其 Wiki 技能与剑盾／长枪动作资源分别核对后映射，资源存在不代表已完成游戏运行兼容测试。

Release 编译无警告，4240 项离线检查通过。覆盖 207 种模型及原生图标资源、全部 802 项名称/时间轴/实际资源、三组特殊 ID、六地图去重、SS 随从主动分类、普通 B 与特殊随从筛选隔离；原47S300项保留。生成、技能、名牌与特殊模型仍需游戏内验证，未声称逐只战斗实测。

## 0.1.0.16 可攻击木人替换

- 核对本机国服 BNpcName：541 为「木人」，13078 为「老朽的木人」。已知普通木人 BNpcBase 901、探索模式 11744、100 级挑战 16845，均从客户端读取模型／比例，不能将 BaseId 当 ModelCharaId。目标仍要求已加载、存活、Combatant、无角色所有者且无游戏变身；未放开宠物、伙伴或装饰家具。
- 既有外观构建抽为共享 HuntAppearanceReplacement，木人与普通目标使用完全同一条路径，模型、中文显示名、Level、Icon 及直接模型倍率都取所选狩猎外观。实际 NameId/BaseId、血量、阵营、主动标志及碰撞半径不改。
- NameplateLevelPolicy 在 scoped OnNamePlateUpdate 中更新国服「90级 」形式的 LevelPrefix；恢复期间使用首次记录的原等级。原生 Character.Level 写入与 UI 字段一并维护，避免已有名牌缓存继续展示木人等级。头标沿用已验证的 61704／61710 显示层策略，不改变木人战斗属性。
- 活动替换记录中，识别原始木人 NameId，且同一完整身份的对象仍可替换时，检测 ModelCharaId／次模型被重置。复用 SwapSession 的两帧重绘流程，先登记 WaitingForDraw 再写入，不重新捕获原快照；失败后保留恢复机会。恢复／移除后不再维护，身份变化沿用已有丢弃策略，不触碰旧槽位。
- 4871 项离线检查通过。对全部 207 种狩猎做模拟原生木人结构的替换／恢复检查，覆盖名字、LevelPrefix、数字等级、目标头标、原版倍率、身份／血量保留；另验证同对象重置、重绘失败重试、重复换模后还原及恢复后不再写入。编译零警告。未在运行中的游戏验证木人各场景的复位时机、特效或模型行为。


## 0.1.0.17 木人入口回归修复与界面精简

用户实测 0.1.0.16 按钮灰色，截图明确显示「当前目标不是可替换的战斗小怪或可攻击木人」，另反馈「当前目标的模型仍在加载」。这说明 v16 的入口仍错误要求普通 Combatant 子类型和已存在的 DrawObject；v16 的离线外观写入／恢复检查未覆盖这两个入口条件，不能证明实际木人可被选入。

- ReplacementTargetPolicy 直接用于 NativeActorAccess.CanApply。已确认原始 BNpcName 541／13078 的 IBattleNpc 可跳过普通 Combatant 子类型及 owner 关联排除；非木人仍维持原有类型、owner、HP、变身和绘制对象限制。木人仍要求战斗 NPC 类型、存活、无变身与完整身份解析，不依据显示名或任意指针猜类型，不写 OwnerId。
- 木人初次外观 Capture/Write 均已经空 DrawObject 安全：Capture 回退对象比例，Write 不解引用空 draw，正常 BeginRedraw/FinishRedraw 重建模型。AnimationCharacter 另加明确 null 判断，避免入口放行后在技能控制中解引用空指针；暂停已有独立模型加载检查。
- 模拟旧失败条件：两种原始名字 ID × 四种子类型，分别验证无绘制对象和父对象关联。另验证普通宠物／队友／已拥有对象／未加载普通怪物继续被拒，死木人和变身木人被拒；原生无绘制对象结构的完整外观准备与还原也纳入检查。
- 主界面折叠辅助功能和长说明。隐藏与替换对象列表用固定 ### ID，数量变化不重置开合；批量隐藏开启边沿只要求收起一次，之后保留用户操作。隐藏列表固定高度 160 滚动，不为这类可逆 UI 排列单独添加镜像测试。
- TargetDiagnostics 记录类型／子类型／原始 NameId／BaseId／owner／HP／模型／绘制加载状态和比例。面板默认折叠，仅通过按钮复制或用户显式 /hms diagnose 输出本机聊天；不自动上传。
- 编译零警告，4893 项离线检查通过。当前无法在用户游戏场景实际点击木人，修复针对已确认的两个拦截条件，不能宣称实机验证通过。


## 0.1.0.18 庭具木人 Type=0 的原位可见模型

实际诊断：庭具 BaseId=901、NameId=541、OwnerId=E0000000、原始 Model=2449、Draw=无、HP=44；替换后 Model=4302、Scale=0.5，但 Draw 仍无、庭具外形不变。木人歼灭战 BaseId=16845、Model=4308、Draw=3、OwnerId 为玩家 ID。用当前国服 Lumina 核对：901 的 ModelChara.Type=0，16845／11744 为 Type=3。因此不再把所有无 DrawObject 木人解释为等待角色模型加载。

新增 LinkedDummyPreview，复用已有的单个 LocalPreview 池，在原目标位置／初始朝向创建不可选中的本地角色模型；原木人的 NameId/BaseId、HP、OwnerId 等不改，显示字段继续走既有备份路径。使用正常本地角色 SetupBNpc，只对本插件创建的代理调用，绝不对网络木人调用 SetupBNpc 或篡改其子类型。原位代理没有真实狩猎身份，仍以 nonce 验证生命周期。真实木人维持为战斗目标，代理负责可见外观、名牌和演示控制。

NativeLinkedDummyAccess 使用已解析角色的 SharedGroupLayoutInstance 字段，在同一完整 ActorIdentity 与同一布局指针上备份 Active／Collider 状态；Hide 时禁用其布局可见性并维持原 Collider 标志，恢复时只向同一来源、同一布局指针写回，布局指针变化或场景切换不使用旧指针。位置每帧从仍存在的原木人取值，不覆盖手动朝向。场景切换只清除旧记录。实际碰撞语义尚需游戏内验证，不能把离线模拟当原生图形集成验证。

NativeActorAccess.FinishRedraw 对活跃原位关联的真实源对象保持隐藏，避免重新显示庭具。名牌数据回调只隐藏该关联源的名牌；代理复用原生本地怪名牌、骨骼高度修正和头条血量隐藏。显式恢复、移除、前方新生成和卸载先恢复动画／气泡等控制，再删除拥有的本地模型，随后请求恢复源外观及原始可见状态。删除失败保留关联以便重试；5 秒始终不就绪则回滚。连续同源替换保留首次原快照及原可见性，避免将自身临时隐藏状态记成原始状态。

ILocalActorAccess 增加 CreateAt，LocalPlacement 先验证有限位置／朝向后再移除旧预览。旧前方生成路径不改位置规则。UsesLayoutModel 根据原始 NameId 和 BNpcBase 对应的 ModelChara.Type 区分源，不能根据替换后 Character.ModelCharaId 推断。诊断新增 Layout／Render 及原位替身自己的 Draw。

4908 项离线检查通过，编译零警告。新增针对原位位置及不可选中、真实源快照、代理 NameId/BaseId=0、正常还原、同帧重复换模、原本隐藏、删除失败重试、5 秒加载超时、来源消失、来源槽位复用、代理 nonce 丢失和场景切换的回归检查。未在运行中的游戏验证本实现，发布为开发测试版。


## 0.1.0.19 庭具原位模型的点击选中

用户确认 0.1.0.18 可见替换成功，但不能点选新模型。原因是上一版 LocalPlacement.Targetable=false，同时原庭具及名牌被隐藏，缺少点击入口。

- 原位代理改为可选中。新增 LinkedDummyTarget，只有候选指针等于当前 Visual.Address，且 LocalPreview.OwnsLive 验证完整身份／nonce 通过时，才把这次选中映射为仍存在的 Source.Address。不解引用来自调用方的任意候选指针。真实来源已消失则返回空目标；代理 nonce 丢失／被其他对象复用时放行外部对象，不把它重新映射到旧木人。清空、其他对象、普通前方预览和已解除关联的旧地址均原样返回。
- NativeLinkedDummyTarget 在显式替换庭具前懒加载 SetHardTarget 与 SetSoftTarget Hook，使用当前安装 FFCS 声明的准确参数（含 this、两个 bool 和 int），保留原始参数并只调用一次 Original。两个 detour 以 readonly 字段强引用，卸载与安装失败路径保证清理。插件加载／仅打开窗口不安装它们。不 Hook 行动管理器，不自动施放技能。
- 本机当前游戏程序离线核对两处签名唯一匹配：SetHardTarget 调用 RVA 0x61DDA5、函数 RVA 0x61DFF0；SetSoftTarget 调用 RVA 0x621A21、函数 RVA 0x61DD10。运行时使用 FFCS 解析地址，不硬编码这些 RVA。上述核对说明接口可定位，不证明实际游戏点选已通过。
- 原位对齐时只向已确认本地代理复制真实木人的 CurrentTargetStatus 显示标记。原木人的 TargetableStatus、身份、OwnerId、HP 和实际目标系统引用不被替身数据覆盖。主界面增加仅在关联存在时显示的「选中木人」按钮及 /hms target，走合法的目标管理器 setter，并验证返回目标身份；不依赖每帧事后改回目标来处理点击。
- 4916 项离线检查通过，编译零警告。覆盖当前代理到真实木人的映射、清空及普通对象不变、无来源不落到复用槽位、代理 nonce 丢失不映射外部对象、解除关联／切图不残留、普通前方预览行为不变及重复换模仅映射当前代理。用户已确认上版可见替换，本版选中效果仍需游戏内验证。

## 0.1.0.20 保留庭具木人的原生可选中状态

用户确认 0.1.0.19 的「选中木人」按钮与直接点击模型均无效。该版修复了代理点击入口，但仍在 BeginRedraw／Hide 中对真实源调用 DisableDraw，并设置 RenderFlags.Model；FinishRedraw 又因 KeepHidden 跳过 EnableDraw。不能将上一版的离线映射测试当成原生可选中验证。

本机 FFCS 7.57.0.9602 反射及当前国服程序离线核对：GameObject、Character、BattleChara 虚表签名均唯一匹配。BattleChara 的 GetIsTargetable vfunc 4（RVA 0x905F00）最终调用 GameObject 判断（RVA 0x897F70）；后者要求 TargetableStatus 0x02／0x08，并检查 RenderFlags。VisibilityFlags.Model=0x02 会命中拒绝掩码；Nameplate=0x800 与 TargetableStatus 0x80 还存在附加判断。DisableDraw 的基类实现（RVA 0x8985D0）会清除 TargetableStatus 的 0xA0，Character 实现（RVA 0x9067D0）调用该路径。因此原源对象保持隐藏模型标志本身就足以阻断原生可选中，禁用绘制也会破坏其正常状态。本实现不直接补写未知位，不对 GetIsTargetable 安装绕过 Hook。接口声明参考 [FFCS GameObject](https://raw.githubusercontent.com/aers/FFXIVClientStructs/main/FFXIVClientStructs/FFXIV/Client/Game/Object/GameObject.cs)，实际偏移与上述判断以安装 SDK／本机 CN 程序为准。

- SwapEntry 增加 PresentationOnly 路径，应用、维护、恢复及卸载只调用 RefreshPresentation，不调用 BeginRedraw／FinishRedraw，也不进行 dummy NeedsRedraw。LinkedDummyPreview 对真实来源保留全部原模型、装备与比例数据，只取目标的 NameBytes／Level／NameplateIcon。可见代理仍走完整外观重绘、自由比例、动画、旋转及 nonce 生命周期。Resize 拒绝只维护显示字段的真实来源，插件照常将用户控制转交代理。
- NativeLinkedDummyAccess.Hide 只控制经完整身份及布局指针核验的 SharedGroupLayoutInstance.SetActive(false)，并维持原 Collider 标志。不再 DisableDraw，不设置真实源的 RenderFlags.Model。还原布局时同样不调用角色 EnableDraw／DisableDraw。移除旧的 KeepHidden 绘制拦截。原源的实际模型类型始终保持 Type=0，避免把怪物模型写到真实庭具却无法加载，实际训练身份／HP／OwnerId 不改。
- 保留上版懒加载的硬／软选中入口映射及手动按钮。诊断新增原生 GetIsTargetable 结果，关联存在时不依赖当前目标，始终显示原木人和代理两份信息。只在本地窗口／显式诊断命令输出，无自动上传。
- 编译零警告，4924 项离线检查通过。新增真实源应用／维护／还原／卸载均无重绘、真实源保留全部比例和模型、目标栏名字／等级／头标仍同步、代理接收目标模型及独立缩放、真实源缩放拒绝的回归检查。其余失效身份、nonce、重复换模和场景生命周期检查继续通过。尚无运行中游戏集成环境，布局隐藏后原生选择与攻击仍需用户实测；发布说明要求先恢复旧替换后重新应用。

## 0.1.0.21 本地开发稿：原位替身战斗姿态

用户确认真实目标可打出伤害，但可见怪物仍为待机，并明确不要求血量下降。现有原位关联只同步位置和选中标记，没有读取战斗／拔刀状态。因此使用实际来源的 InCombat／IsWeaponDrawn，不通过 HP 差推断命中，不改变真实血量。用户随后要求先别发包，本稿不打包、不上传，在线版仍为 0.1.0.20。

安装 SDK 反射确认 Character.InCombat 可读写，Character.IsWeaponDrawn 只读、TimelineContainer.IsWeaponDrawn 可读写。原生位字段对应 CharacterData.Flags 的 bit1 及 Timeline.Flags3 的 bit6。通过生成 setter 只向当前通过完整身份和 nonce 核验的本地替身写两个标志，不复制整个 Flags，不改来源战斗状态、HP、OwnerId、身份、ModelState、BaseOverride、动画速度或手动旋转。接口参考 [FFCS CharacterData](https://raw.githubusercontent.com/aers/FFXIVClientStructs/main/FFXIVClientStructs/FFXIV/Client/Game/Character/CharacterData.cs) 与 [TimelineContainer](https://raw.githubusercontent.com/aers/FFXIVClientStructs/main/FFXIVClientStructs/FFXIV/Client/Game/Character/TimelineContainer.cs)，实际布局按安装版本验证。

LinkedDummyPreview 读取并维护 CombatPresentation：有战斗标志即要求战斗姿态，不依赖庭具是否真的拔出武器。进入／退出时分别请求当前国服 ActionTimeline 34（battle/idle）／3（normal/idle），持续状态不反复重播。普通待机新生成不额外播动作。未加载时不写姿态，重新就绪会初始化最新状态；暂停或布弗鲁连招期间延迟，结束后采用最新状态，不回放暂停期间的多次变化。解除、场景遗忘、来源消失或 nonce 丢失后停止写入。诊断增加战斗、拔刀与基础槽动作编号，留本机可见。

首轮编译零警告，4942 项离线检查通过。新增 18 项覆盖进入／退出、稳定状态不重播、无 HP 依赖、暂停延迟与恢复、暂停中状态往返、模型重载、清理、重复替换、nonce 丢失、两处位字段保留、身份／血量／BaseOverride 不变，以及当前国服通用时间轴 Key 校验。游戏实际模型资源与姿态混合尚未实机验证。用户随后授权完成以下五项本地增强，继续要求不发包。

### 五项本地增强（未打包、未上传）

- 提取 CombatPose，三档 Automatic／Battle／Idle 同时覆盖原位替身和普通前方预览。自动模式对前方预览使用待机，强制模式独立于来源；所有原生写入仍由当前完整租约／nonce 核验守护。模式、模型或租约改变时初始化姿态；暂停或手动动作优先时只保留最新选择；写入失败不提前提交缓存，后续重试。普通源怪物继续由游戏维护真实战斗状态，不向它写伪造战斗位。
- ActionPreview 保存按对象及模型隔离的手动动作记录，启动保护 750ms 只覆盖时间轴尚未建好的一段时间，不作为动作总时长。之后通过当前角色 14 个 TimelineIds 中的精确动作 ID 及非空原生 SchedulerTimeline 判定仍在播放。暂停时保留保护时间；每帧维护，结束后释放自动姿态。Play 本身拒绝暂停对象，单次技能会取消当前连招；连招仍拥有自动姿态之上的优先级。取消／恢复／删除前清除本地记录，防止相同地址及同一合成对象 ID 被新租约复用。
- 一键重置通过既有受保护控制接口依次取消连招和演示气泡、恢复暂停前时钟／修复残留零速、停止本插件正在播放的精确时间轴槽、还原所用狩猎外观的原版倍率及首次记录朝向，本地预览回到自动姿态。不删除或重新生成模型，不重写真实 HP、OwnerId 和身份。DemoDefaults 在换模型、删除、恢复及切图时维护边界，同模型不同外观更新原版倍率但保留初始朝向，新租约删除旧默认值。
- UserPreferences 仅保存稳定名字 ID／Base ID、筛选、每怪倍率、生成距离、姿态、悬浮窗及主要折叠区；绝不保存地址、ActorIdentity、nonce、隐藏或暂停租约。旧配置默认可读，非法枚举、地图、ID 键、无效比例及显式 null 集合均处理。UI 生成独立快照，Framework 合并延迟 750ms 写配置，卸载补存。隐藏所有小怪开启时的一次收起优先于记忆状态，随后保留用户再次展开的操作。
- SwapSession 增加修订号、不可变条目快照复用及字典精确查询，正常逐帧展示刷新不再重建条目数组。保护集合按条目修订变化刷新，批量候选稳定时 250ms 扫描、成员变化即刻重扫；缓存对象每次使用仍重新核验完整身份。UI 快照稳定时 100ms 更新，操作与目标改变立即更新；窗口关闭时不构建详细诊断，显式 diagnose 始终即时读取。怪物筛选仅在条件变化时重算。动画、暂停、旋转、所有权和布局维护仍逐帧执行，未声称实测 FPS 提升。

当前国服程序离线核对新使用接口：PlayTimeline 特征唯一匹配，调用 RVA 0x8BE73F、函数 RVA 0x606AB0；SetSlotTimeline 特征唯一匹配，调用 RVA 0x6087F4、函数 RVA 0x6084C0。后者反汇编确认检查 slot < 14，写入新时间轴 ID 并释放当前槽位的调度器引用，ID 0 用于清槽。本实现仅清除本插件追踪的确切动作 ID，保留其他槽位。运行时仍使用安装 SDK 的解析地址，不硬编码 RVA。

最终编译零警告，4990 项离线检查通过。增强检查覆盖普通前方预览三档、模式延迟／恢复、不同模型和租约、失败重试、长动作无固定时长截断、暂停优先、启动时间保留、停止与失效对象、重置默认值、实际 JSON 往返／旧配置／null／非法值、扫描门限、成员即时失效，以及稳定快照复用与旧 UI 快照不被改写。三份原始技能／台词资料未变，在线订阅仍为 0.1.0.20。未生成 0.1.0.21 发行包，未上传；暂无运行中游戏可验证界面与具体模型混合效果。

### 0.1.0.21 发布准备

用户随后明确要求「上传」，解除先前暂缓发包的要求。按当前五项增强内容生成 0.1.0.21 开发测试版，保留游戏内验证限制；发布说明及订阅更新为本版，原始技能／台词资料不变。重新执行构建与完整离线检查，打包前核对版本及发行内容，上传后核对公开下载包哈希、源码标签与在线订阅。


## 0.1.0.22：战斗普通攻击演示

用户反馈战斗姿态只有战斗待机，没有向玩家挥击的平A。本版在独立控制器中定时请求普通攻击时间轴，仅作用于现有一个本地预览（前方或庭具替身），不添加服务器 AI，不调用 ActionManager、不写真实木人血量或目标身份。

从当前国服 ActionTimeline 与实际 TMB 核对：112=`battle/auto_attack1`、121=`battle/auto_attack1_mon_a`、124=`battle/auto_attack1_mon_b`、127=`battle/auto_attack_shot1_mon`，均为 slot 1。前三者引用 `cbbm_atk1`，远程引用 `cbbm_sht1`。不是按技能名称随意选一条 Action 记录：大量名为「攻击」的 Action.AnimationEnd 是 0xFFFFFFFF 等特殊值，不能直接当作可播放的时间轴 ID。没有从 BNpcBase 读出可靠的逐怪攻速或近战／远程平A映射，因此默认通用时间轴，提供其余版本手动试播，间隔明确作为演示设置。已确认 TMB 存在不等于 207 个模型的片段均已实测。

默认开启、间隔 2500ms，支持 1000～10000ms。仅有效战斗状态下运行，每次挥击前重新读取玩家坐标，使用 atan2(dx,dz) 水平转向；手动固定朝向优先。只读玩家位置，不给玩家、真实目标或联网怪物写动作。停止时只清除本控制器已请求的确切时间轴，原生访问再次核验完整身份、ClientObjectManager／槽位／nonce、模型与加载状态。

优先级为暂停 > 手动技能／连招 > 自动平A，姿态控制仍保留既有的延迟切换。暂停不停止当前挥击、不转向、不累积计时，恢复帧排除暂停时长；手动动作通过 ActionPreview.BeforePlay 先中断自动挥击，实际原生时间轴未结束就持续保护，恢复自动播放前重新等待完整间隔。卡顿不补播。单次平A沿用手动播放器，但明确设置一个间隔的播放上限，只对该次试播生效，防止驻留循环永久占用手动优先级；其他手动长技能不设固定截断。

关闭与脱战清除当前自动动作；删除、恢复、换模、切图及 nonce 失效清理计时和租约，不对复用对象补写。平A选项纳入现有 JSON 偏好校验，旧配置使用有效默认值，不持久化对象引用。自动维护异常后暂停该循环，调整选项可重试，避免逐帧异常刷屏。

构建零警告／零错误，5037 项离线检查通过。新增检查覆盖时序边界、卡顿不补播、暂停／恢复和延迟取消、真实长手动动作优先、固定朝向、驻留试播自动结束、缺失玩家、重绘／加载、模型变化、nonce 复用、恢复／场景清理、偏好往返及四条国服时间轴的 key／slot／TMB。没有运行中的游戏环境，不能据此宣称已看到各怪物的实际平A效果。三份技能／台词资料保持不变。

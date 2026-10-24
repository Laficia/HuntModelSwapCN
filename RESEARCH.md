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

## 主要参考

- [Glamourer 模型编辑入口](https://github.com/Ottermandias/Glamourer/blob/e165910345b17b99d9aa35990005b5e0f73ae65b/Glamourer/State/StateEditor.cs)
- [Glamourer 状态 IPC](https://github.com/Ottermandias/Glamourer/blob/e165910345b17b99d9aa35990005b5e0f73ae65b/Glamourer/Api/StateApi.cs)
- [FFXIVClientStructs ModelContainer](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Character/ModelContainer.cs)
- [NotoriousMonster 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/NotoriousMonster.yml)
- [BNpcBase 数据结构](https://github.com/xivdev/EXDSchema/blob/latest/BNpcBase.yml)
- [MonsterModelSwap 换模服务](https://github.com/Jayty07/MonsterModelSwap/blob/main/MonsterModelSwap/Services/ModelSwapService.cs)（原实现针对玩家，不能直接当作普通 NPC 换模成品）。

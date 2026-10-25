# 技能资料来源

S 怪技能名称与归属核对自[最终幻想 XIV 中文维基（灰机 Wiki）](https://ff14.huijiwiki.com/)。每个条目的具体页面保存在 `Catalog/SRankSkillProfiles.json` 的 `WikiUrl`，插件中可打开该页面。2026-10-25 通过正常浏览器逐页核对了 47 种常规 S 怪；只整理技能名称/归属等游戏数据事实，不收录攻略正文或图片。Wiki 页面注明内容遵循 [CC BY-NC-SA 3.0](https://creativecommons.org/licenses/by-nc-sa/3.0/)。游戏名称、技能名称及资源由 SQUARE ENIX 与相关权利人所有。

动作 ID、中文名称、时间轴及资源存在性按本机国服客户端表核对。唯一资源对应不等于已经确认该怪的原始 Action ID；资料明确区分战斗记录映射、资源映射、多版本候选和无独立动作。

S 怪台词归属于 2026-10-25 通过正常浏览器逐页核对上述 47 只怪物的页面；明确收录台词的五只的页面链接保存在 `Catalog/SRankDialogueProfiles.json`。台词正文由本机国服 `NpcYell` 表读取，JSON 中保留行号与文本校验值，游戏台词版权归原权利人所有。不收录 Wiki 攻略正文或图片。阿提卡斯 16874～16903 的方向台词变体 ID 还参考下述固定提交的 `BossMod.Modules/Dawntrail/Hunt/RankS/Atticus.cs`，只使用 ID 归属事实，没有引入其机制代码。其余三句由用户实战文本和 Wiki／客户端共同核对。

部分技能的 NPC/Action ID 关系参考 [BossMod](https://github.com/awgil/ffxiv_bossmod)，提交 `4fc3798e283432e2bac33130f48769ba778a0eba`。具体源文件链接保存在各条目的 `RecordSource`。仅提取并核对动作 ID 事实，没有引入其战斗模块、机制处理、自动移动或施法实现。以下保留该项目的许可声明：

```text
BSD 3-Clause License

Copyright (c) 2022-2024, Andrew Gilewsky

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

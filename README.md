# 巨型水箱 / Mega Water Tank

> 工作区在研 mod。**当前 v0.9.5（2026-11-20），已构建 + 已部署 + 多轮实机通过**。
> 身份：`manifest.xml` `<ID>megatank` / 程序集 `MegaTank.dll` / 作者 核桃 / 部署目录 `Mods\megatank\`。
> 尚未建根仓 gitlink（工作目录未纳入子模块）；发布前再定。

## 它做什么

一个 1.9 吨主池 + 16 个各 1.9 吨内胆槽（满配 32.3 吨）的巨型储水装置。水走原版容器语义：
水培机 / 净水机 / 水质检测仪 / 水龙头都按原版规则识别，不需要额外设置。

- **R1 溢出**：主池满后继续灌，多出的水进空内胆
- **R2 回补**：主池低于 90% 自动从内胆抽回，帧末统一刷新（约 0.25 s 一次）
- **水质着色（v0.5.0）**：主池/内胆按内容物实时变色，复用原版 `WaterHelper.GetLiquidContainerColor`
  的平均色算法，**0 个新增补丁**，逐字节一致
- **电话服务**：快速拨号 `21474836`（冲突时退让），三条可复购服务（水箱 500 / 内胆 250 冷却 1 天；
  扩容 150 无冷却），见 `src/Phone/Services.cs`
- **三语**：zh / en / ru，走官方本地化表 + Ready 闸（返回英文优先于读未就绪的 locale）
- **日志开关**：主菜单 `Diagnostics → Verbose log`（默认关）。`Log.Info` 门控、`Log.Once`
  警告始终输出；配置在 **`IMod.Init` 加载期**定义（主菜单面板才能列出），
  section 用字面量、label 走 lambda，加载期不读语言

玩家可见说明见 [`PlayerReadme.md`](PlayerReadme.md) / [`PlayerReadme.en.md`](PlayerReadme.en.md)。

## 版本与文档入口

| 要什么 | 去哪 |
|---|---|
| 逐版改动与技术细节 | [`CHANGELOG.md`](CHANGELOG.md)（0.1 → 0.9.5） |
| 当前状态 / 验证证据 / 未验项 | [`STATUS.md`](STATUS.md) |
| **启停与卸载验收单（P4-04，待跑）** | [`docs/验收单-P4-04-启停与卸载.md`](docs/验收单-P4-04-启停与卸载.md) || 踩坑账 / 开发经验（开工必读） | [`docs/LESSONS.md`](docs/LESSONS.md) |
| 设计硬约束 / 否决方案 / 信心分级 | `..\docs\方案-巨型水箱mod.md` |
| 排工与批次出口条件 | `..\docs\排工-巨型水箱mod.md` |
| 水质着色机制与方案 A | `..\docs\可行性-水质贴图着色.md` |
| Workshop 同类 mod 可借鉴点与合规边界 | `..\docs\分析-workshop优化mod可借鉴思路.md` |
| 游戏侧水/液体机制（1.0.0.16 源码级） | `..\docs\domain\D07-水与液体系统.md` + `..\game-kb\items\水与液体容器.md` |
| 构建→部署→验证门禁口径 | `..\AGENTS.md` + `..\docs\工作区工作流标准.md` |

## 构建 / 验证 / 部署

```bash
# 只验编译，不碰游戏目录
dotnet build MegaTank.csproj -c Release -p:SkipDeploy=true

# 纯函数边界测试（32 条断言，dotnet run 退出码即结果）
dotnet run --project tests/PureCheck/PureCheck.csproj

# 构建期自动门禁（no skip）：版本同步 → 玩家文档三件套 → 符号核对 → CopyToMods
# 跳过部署：加 -p:SkipDeploy=true；跳过符号核对：加 -p:SkipSymbolCheck=true
```

- 入口：`src/Mod.cs` 的 `ModEntry : IMod`（`Init` / `OnEnable` / `OnDisable` 逐项 `Step()` 兜底）。
- **没有 `[HarmonyPatch]` 特性补丁**：5 个补丁由 `src/Core/LatePatches.cs` 在
  `OnGameLoadedLate`（本地化就绪）后手动安装并核对 owner；加载期 PatchAll 会触发 `Liquid` 静态
  本地化 cctor 并永久损坏本地化（见 `src/Mod.cs` 头部注释）。
  **第 6 个补丁（配置面板语言）例外，装在 `OnEnable`**：主菜单 Modding 面板进档前就能打开，
  等 `OnGameLoadedLate` 就晚了；目标 `ModConfigWindow` 无静态字段，提前装无 cctor 风险
  （见 `src/Patches/ConfigSectionPatch.cs` 头注释与 `docs/LESSONS.md` A6）。
- 部署布局：`Mods\megatank\` 下 `MegaTank.dll` + `manifest.xml` + `CHANGELOG.md` +
  两份 PlayerReadme + `Localization\` + `Textures\`。**贴图只在 `mod.Sprites[纯文件名]`**，
  不放 `mod.Textures`；`spritePath` 用纯文件名，先 `SetSpriteAndShapeFromMod` 再 `SetShape`。
- 目标游戏：**Probably Stolen 1.0.0.16**（Unity Mono / 官方第一方加载器）；manifest
  `<GameVersions>` 写前缀 `1.0`。`Probably Stolen Demo` 是 IL2CPP，**不能放 Mono DLL**。

## 纪律

- 遵守工作区根 `AGENTS.md`；本仓专属规则见 [`AGENTS.md`](AGENTS.md)。
- 版本号按工作区规则升并全套同步：`manifest.xml` / `src/Core/Ids.cs` 的 `Ids.Version` /
  `release.profile.json` / 两份 PlayerReadme / `CHANGELOG.md`；同时更新 manifest 的中英 Description。
- 自动启动或重启游戏、发布类操作 → 先给方案 + diff 预览，用户点头再动。
- 存档影响：水箱物品按原版 `SaveItemNode` 存档，但"用 mod 图"的标志不进档 ⇒ 载入后靠
  `AttachLoadHook` 强制重贴（见 CHANGELOG 0.2.0）；`useSpriteFromMod` 不写档是硬事实，勿改判。

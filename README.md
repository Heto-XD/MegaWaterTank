# 工作区总索引 · `D:\games\open code work`

> PS 游戏（Probably Stolen **1.0.0 正式版**；Demo/IL2CPP 形态仅作历史参考）**多 mod 仓 + 工具脚本**的集合。本仓（superproject）**无 remote，永不上云**。
> 动手前先读 `AGENTS.md`（规则）和 `docs\工作区目录规范.md`（目录/部署纪律）。

## 先看这四份

| 文件 | 内容 |
|---|---|
| `AGENTS.md` | 工作区规则：什么要先问、什么直接做、本机坑、反编译库红线 |
| `docs\游戏资料索引.md` | **游戏源码 + 官方 kit 的统一查法**（按任务 → 文件:行号 / 章节），含事实校正表 |
| `docs\工作区目录规范.md` | 根目录白名单、mod 仓结构、**官方加载器部署纪律**（`Mods\<模组名>\` + manifest）、收尾检查 |
| `docs\README.md` | **文档总索引 + 事实等级（L1 权威 / L2 实证 / L3 推断 / L4 历史）**；先看这页再点开别的文档 |

## 常用入口

| 要做的事 | 命令 / 路径 |
|---|---|
| 编译 + 部署某个 mod（主 DLL + Core DLL 一起进 `Mods\`） | `dotnet build <Mod>\src\<Mod>.csproj -c Release` |
| 只验编译、不碰游戏目录 | 追加 `-p:SkipDeploy=true` |
| 部署体检（官方加载器布局：manifest/清单/dll/捆绑/pdb/版本漂移；只读，exit 1 = 会被加载器拒绝） | `pwsh -NoProfile -File _modkit\check-deploy.ps1` |
| 补丁目标本地化门禁（只读，exit 1 = 该补丁会把游戏本地化打崩） | `pwsh -NoProfile -File _modkit\check-patch-cctor.ps1`（`-SelfTest` 自证；`-Projects A,B` 指定仓） |
| 发布 zip | `_release\publish.ps1`（产物 → `dist\<Product>-v<版本>.zip`；推远端默认停用） |
| 目录审计 | `_modkit\audit-mods.ps1` |
| PowerManager 回归（现存唯一 `_regress`） | `PowerManager\_regress\regress.ps1` |
| 游戏源码 / 反编译结论 | `probably stolen 1.0.0\`（`Assembly-CSharp.dll` + `GAME_DATA_INDEX.md`；931 个 `.cs` 已归档 `_decomp\src_1.0.0.5.zip`，分版本源码 `_decomp\1.0.0.5\`/`1.0.0.6\`，本地专属，禁止外传）；查法见 `docs\游戏资料索引.md` |

## mod 仓

`BlackPrinter` `ContainerManager` `ContainerMerchant` `EmptyNukeBarrel` `ItemProbe` `ItemStacking`
`NewbieGift` `PowerManager` `RealPrinter` `SecretAdmirerMod` `SentinelTracker`
（旧仓 BuyHouse / ContainerSuite / PowerProbe / ratprinter / p特性拓展 已于 2026-10-31~11-02 迁出或停用；完整快照 = 根目录同名 `*.zip`，不再展开目录。）

## 工具 / 资料 / 临时

- 工具：`_modkit\` `_release\`
- 文档：`docs\`（跨仓）· 各仓 `docs\`（专属）
- 知识库：`game-kb\`（条目化证据库，**独立 git 仓**）/ `docs\游戏资料索引.md`（源码 + kit 查法索引）/ `ObsidianVault\`（方法论笔记）/ `probably stolen 1.0.0\`（游戏源码，**永不上云**）
- 外部资料：`mod模板\`（可构建脚手架，`scripts\new-mod.ps1` 生成新仓）/ `templates\ModProject\`（官方壳结构母版，占位不可构建）/ `probably-stolen-modding-main\`（官方 kit）/ `开发者提示词指南\`（外部教材）/ `何小鲁框架正式版本\`（他人框架副本，其 `docs\`/`knowledge\` 只服务该目录自身，工作区主链以 `docs\` 为准）
- 工具：`_modkit\` `_release\` `_decomp\`（分版本反编译 + `dump.ps1`/`diff.ps1`）
- 探针/临时/本地归档（`_` 前缀 = 可随时重建）：`_perkanalyze\` `_refpack\` `_sigprobe\` `_probe_api\` `_review\` `_archive\`；`ItemDump\`（运行时导出，可重跑再生）

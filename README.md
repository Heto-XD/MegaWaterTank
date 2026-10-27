# 工作区总索引 · `D:\games\open code work`

> PS 游戏（Probably Stolen Demo）**多 mod 仓 + 工具脚本**的集合。本仓（superproject）**无 remote，永不上云**。
> 动手前先读 `AGENTS.md`（规则）和 `docs\工作区目录规范.md`（目录/部署纪律）。

## 先看这三份

| 文件 | 内容 |
|---|---|
| `AGENTS.md` | 工作区规则：什么要先问、什么直接做、本机坑、反编译库红线 |
| `docs\工作区目录规范.md` | 根目录白名单、mod 仓 canonical 结构、R8 双 DLL 部署纪律、现状偏差表 |
| `docs\Migration_Playbook.md` | Core 拆分迁移手册（13 仓已迁移） |

## 常用入口

| 要做的事 | 命令 / 路径 |
|---|---|
| 编译 + 部署某个 mod（主 DLL + Core DLL 一起进 `Mods\`） | `dotnet build <Mod>\src\<Mod>.csproj -c Release` |
| 只验编译、不碰游戏目录 | 追加 `-p:SkipDeploy=true` |
| 部署完整性体检（13 仓，只读，exit 1 = 不能双 DLL 交付） | `pwsh -NoProfile -File _modkit\check-deploy.ps1` |
| 发布 zip | `_release\publish.ps1`（产物 → `dist\<Product>-v<版本>.zip`；推远端默认停用） |
| 目录审计 | `_modkit\audit-mods.ps1` |
| ContainerSuite 回归 | `ContainerSuite\_regress\regress.ps1` |
| 反编译数据库（字段/RVA/dump） | `游戏数据反编译\`（本地专属，禁止外传） |

## mod 仓

`BlackPrinter` `BuyHouse` `ContainerSuite` `EmptyNukeBarrel` `ItemProbe` `ItemStacking` `NewbieGift`
`PowerManager` `PowerProbe` `RealPrinter` `ratprinter`(= custom_rat_printer) `SecretAdmirerMod`
`SentinelTracker` `p特性拓展（核桃）`(= PPerkPack)

## 工具 / 资料 / 临时

- 工具：`_modkit\` `_release\`
- 文档：`docs\`（跨仓）· 各仓 `docs\`（专属）
- 知识库：`游戏数据反编译\`（**永不上云**）
- 外部资料：`mod模板\` `probably-stolen-modding-main\` `开发者提示词指南\` `UnityExplorer汉化\` `自定义拾荒窗口\` `自动测试mod工具\`
- 探针/临时（`_` 前缀 = 可随时重建）：`_perkanalyze\` `_refpack\` `_sigprobe\` `_probe_api\` `_review\`

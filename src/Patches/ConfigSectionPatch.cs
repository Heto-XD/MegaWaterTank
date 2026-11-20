// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Reflection;
using HarmonyLib;

namespace MegaTank
{
    /// <summary>
    /// 配置面板打开前改写分组名 / 补开本地化闸 —— **在 OnEnable（加载期）就安装**，
    /// 不能等 OnGameLoadedLate。
    ///
    /// 0.8.2 的教训（实机 Player.log + 截图实证）：
    ///   补丁原本挂在 `LatePatches`（OnGameLoadedLate）。但**主菜单的 Modding 面板
    ///   在进档前就能打开**——主菜单 → Modding → 选中 mod → 齿轮 → `ModConfigWindow.Open`。
    ///   那时 `OnGameLoadedLate` 还没触发 ⇒ 补丁没装 ⇒ 分组名停在并排原样
    ///   「DIAGNOSTICS / 诊断」、标签因 Ready 闸未开仍是 "Verbose log"。
    ///   日志可证：`延迟补丁 5/5 就绪` 出现在读档之后，而面板是在那之前打开的。
    ///
    /// 为什么提前装是安全的（与 WaterHelper 那批补丁不同）：
    ///   - `ModConfigWindow` 是纯静态类，**没有任何静态字段 / 静态构造**
    ///     ⇒ Harmony 准备 detour 时不会跑危险 .cctor（对比 Liquid：静态字段初始化器读本地化）。
    ///   - `Open()` 方法体只引用 ModConfig / CustomUIManager / StoreUIManager / CustomUIBuilder，
    ///     均不在 `check-patch-cctor -Verbose` 报出的 7 个危险 cctor 名单里。
    ///   - 同一做法已在 ContainerManager 2.16.0 上线（`SectionLocalePatch.Install()` 在 OnEnable）。
    ///
    /// 前缀里做两件事：
    ///   1. `Text.MarkReady()`：面板能打开 ⇒ 主菜单已 yield 完
    ///      `LocalizationSettings.InitializationOperation`，此刻开闸安全，标签才会跟语言。
    ///   2. `CfgLocale.ApplyTo`：把 Section 从并排字面量改成当前语言的单语。
    /// </summary>
    internal static class ConfigSectionPatch
    {
        private const string HarmonyId = "mod.megatank.cfgsection";

        private static Harmony _harmony;
        private static bool _installed;

        /// <summary>幂等安装。OnEnable 先装；LatePatches 再调一次作兜底（已装则立即返回 true）。</summary>
        internal static bool Install()
        {
            if (_installed && _harmony != null) return true;

            try
            {
                MethodInfo target = AccessTools.Method(typeof(global::ModConfigWindow), "Open",
                    new[] { typeof(global::LoadedMod) });
                if (target == null)
                {
                    Log.Once("target-cfgsection",
                        "找不到 ModConfigWindow.Open(LoadedMod)，配置分组名保持「English / 中文」并排（其余正常）");
                    return false;
                }

                if (_harmony == null) _harmony = new Harmony(HarmonyId);

                _harmony.Patch(target,
                    prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(ConfigSectionPatch), nameof(Prefix))));

                // 自检：确认我方 owner 真的挂在该方法上
                var info = Harmony.GetPatchInfo(target);
                bool mine = info != null && info.Owners != null && info.Owners.Contains(HarmonyId);
                if (!mine)
                {
                    Log.Once("verify-cfgsection",
                        "配置分组名补丁未找到我方 owner（" + HarmonyId + "），可能被其它 mod 覆盖");
                    return false;
                }

                _installed = true;
                Log.Info("配置分组名补丁已安装（加载期，主菜单即可生效）");
                return true;
            }
            catch (Exception e)
            {
                Log.Once("cfg-section-install", "配置分组名补丁安装异常（降级为并排原样）：" + e.Message);
                return false;
            }
        }

        internal static void Uninstall()
        {
            try { _harmony?.UnpatchAll(HarmonyId); } catch { }
            _harmony = null;
            _installed = false;
        }

        /// <summary>Open(LoadedMod mod)：只处理本 mod，其它 mod 的面板原样放行。</summary>
        internal static void Prefix(global::LoadedMod mod)
        {
            try
            {
                if (mod?.Manifest == null) return;
                if (!string.Equals(mod.Manifest.ID, Ids.ModId, StringComparison.Ordinal)) return;

                Text.MarkReady();
            }
            catch (Exception e)
            {
                Log.Once("cfg-ready", "开面板时本地化闸补开失败（按英文兜底）：" + e.Message);
            }

            try
            {
                CfgLocale.ApplyTo(mod.Manifest.ID);
            }
            catch (Exception e)
            {
                Log.Once("cfg-apply", "配置分组名改写异常（保持并排原样）：" + e.Message);
            }
        }
    }
}

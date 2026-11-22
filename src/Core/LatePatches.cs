// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MegaTank
{
    /// <summary>
    /// 延迟安装补丁：等 ModHook.OnGameLoadedLate（本地化已就绪）之后再 patch。
    ///
    /// 为什么不能靠 [HarmonyPatch] 特性自动装：
    ///   加载器在 Awake 阶段 PatchAll，准备 detour 时会解析目标方法体引用的静态字段所属类型，
    ///   进而执行其 .cctor。WaterHelper.AddWater 的方法体读 Liquid 静态字段，而 Liquid 的 cctor
    ///   调 LocHelper → 本地化未就绪 → 抛异常 → 该类型 cctor 被标记失败并缓存 →
    ///   整个进程内 Liquid 永久损坏（水系统全挂）。
    ///
    /// tooltip 自 0.12.0 起改走官方 `ModHook.OnCreateTooltipLate`（见 TankTooltip），
    /// 不再需要 Harmony 补丁，所以这里只剩 3 个目标。
    ///
    /// 代价：本文件里的补丁对 _modkit\check-patch-cctor.ps1 不可见（它只扫 [HarmonyPatch] 特性）。
    ///   所以装完必须**自检一次**：目标存在 + 我方 owner 真的挂上了（GetPatchInfo）。
    /// </summary>
    internal static class LatePatches
    {
        private const string HarmonyId = "mod.megatank.runtime";

        private static Harmony _harmony;
        private static readonly List<string> Applied = new List<string>();

        internal static void Install()
        {
            if (_harmony != null) return;

            try
            {
                _harmony = new Harmony(HarmonyId);
            }
            catch (Exception e)
            {
                Log.Once("late-init", "Harmony 实例创建失败：" + e.Message);
                return;
            }

            Applied.Clear();
            Try("R1 溢出", PatchAddWater);
            Try("电话名", PatchPhoneName);
            Try("存盘摘名片", PatchSaveStrip);

            Log.Info("延迟补丁 " + Applied.Count + "/3 就绪" +
                     (Applied.Count > 0 ? "：" + string.Join("、", Applied.ToArray()) : "（全部失败，功能降级）"));
        }

        internal static void Uninstall()
        {
            if (_harmony == null) return;
            try
            {
                _harmony.UnpatchAll(HarmonyId);
                Log.Info("延迟补丁已卸载（" + Applied.Count + " 个）");
            }
            catch (Exception e)
            {
                Log.Once("late-unpatch", "卸载补丁失败：" + e.Message);
            }
            finally
            {
                _harmony = null;
                Applied.Clear();
            }
        }

        private static void Try(string label, Func<bool> patch)
        {
            try
            {
                if (patch()) Applied.Add(label);
            }
            catch (Exception e)
            {
                Log.Once("late-" + label, label + " 安装异常（降级）" + "：" + e.Message);
            }
        }

        private static bool PatchAddWater()
        {
            MethodInfo target = AccessTools.Method(
                typeof(WaterHelper),
                "AddWater",
                new[]
                {
                    typeof(GameItem), typeof(int), typeof(int),
                    typeof(bool), typeof(int), typeof(int), typeof(bool)
                });

            if (target == null)
            {
                Log.Once("target-addwater", "找不到 WaterHelper.AddWater(7 参数)，R1 溢出停用（其余功能正常）");
                return false;
            }

            _harmony.Patch(target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(OverflowPatch), nameof(OverflowPatch.Prefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(OverflowPatch), nameof(OverflowPatch.Postfix))));

            return Verify(target, "AddWater");
        }

        private static bool PatchPhoneName()
        {
            MethodInfo target = AccessTools.Method(typeof(StorePhoneClient), "GetLocalizedDisplayName");
            if (target == null)
            {
                Log.Once("target-phonename", "找不到 StorePhoneClient.GetLocalizedDisplayName，电话簿显示原始键（其余功能正常）");
                return false;
            }

            _harmony.Patch(target,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(PhoneName), nameof(PhoneName.Postfix))));

            return Verify(target, "GetLocalizedDisplayName");
        }

        /// <summary>
        /// 存盘前后摘/放电话名片：**只挂 Prefix + Finalizer，不跳过原方法**。
        /// 目标是多 mod 共用的热方法（根 AGENTS 硬规则 27），这里不改任何存档字段，
        /// 只是让序列化那一刻字典里没有我们那条（见 SaveStripPatch 头注释）。
        /// </summary>
        private static bool PatchSaveStrip()
        {
            MethodInfo target = AccessTools.Method(typeof(PlayerStore), "SaveGame", Type.EmptyTypes);
            if (target == null)
            {
                Log.Once("target-savestrip",
                    "找不到 PlayerStore.SaveGame，电话名片会随存档持久化（停用后可能残留一条，其余正常）");
                return false;
            }

            _harmony.Patch(target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(SaveStripPatch), nameof(SaveStripPatch.Prefix))),
                finalizer: new HarmonyMethod(AccessTools.Method(typeof(SaveStripPatch), nameof(SaveStripPatch.Finalizer))));

            return Verify(target, "SaveGame");
        }

        /// <summary>
        /// 补丁自检：确认我方 owner 真的在该方法上。
        /// 只判"目标存在"不够——Harmony 允许 Patch 成功但 AttributePatch 被别的 mod 覆盖；
        /// 只判"挂上"也不够——目标方法改名时 AccessTools 返回 null，上面已经早退。
        /// </summary>
        private static bool Verify(MethodBase target, string label)
        {
            try
            {
                var info = Harmony.GetPatchInfo(target);
                if (info == null)
                {
                    Log.Once("verify-" + label, label + "：补丁信息为空，视为未挂上");
                    return false;
                }

                bool mine = info.Owners != null && info.Owners.Contains(HarmonyId);
                if (!mine)
                {
                    Log.Once("verify-" + label, label + "：未找到我方 owner（" + HarmonyId + "），请检查是否被其它 mod 覆盖");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Once("verify-" + label, label + " 自检失败：" + e.Message);
                return false;
            }
        }
    }
}

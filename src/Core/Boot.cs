// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// 进档编排：所有"必须等游戏就绪"的安装都在这里，按依赖顺序执行，每步独立兜底。
    /// 由 `ModHook.OnGameLoadedLate` 触发（本地化、存档、UI 都就绪），每次进档都会调，逐步幂等。
    /// </summary>
    internal static class Boot
    {
        internal static void OnLoadedLate()
        {
            // 先开本地化闸：后面的补丁/名片/服务都要取文案
            Step("本地化闸", Text.MarkReady);

            // 配置已在 IMod.Init（加载期）定义，此处不需要也不能再定义——
            // 主菜单面板靠加载期的实例判定 HasSettings。
            Step("补丁", LatePatches.Install);
            Step("电话名片", PhoneCard.Install);
            Step("容器索引", TankRegistry.RefreshFromStore);
            Step("水库状态", ReservoirRuntime.Clear);

            // 扩容等级：先迁移老档口径（0.9.x → 0.10.0，等级 +8 保住容量），
            // 再清缓存，最后让老容器对齐当前档位。
            // 放在服务注册之前——服务注册要看 CanUpgrade 决定满级置灰。
            Step("扩容口径", Capacity.EnsureScheme);
            Step("扩容等级", () =>
            {
                Capacity.Invalidate();
                Capacity.ApplyAll();
            });

            // 0.12.0 原版属性对齐：基础价 + NOT_FOR_RESALE 可见类型 + 旧 CONTAINER_TAG 组合。
            // 放在容量之后：两者都只改 tag，互不依赖；老档容器在这里一次校正到位。
            Step("原版属性对齐", () => TankAttributes.ApplyAll());

            // 0.14.0：读档后把机器槽水箱回填到满（默认夜间模式）；Continuous 模式会在 Tick 里自己维持。
            Step("机器预腾回填", TankRuntime.RestoreOnLoad);

            Step("服务注册", Services.EnsureRegistered);
        }

        /// <summary>
        /// 新开局（NewGameData.FireOnNewGameStarted）。
        ///
        /// ★ **新开局不会触发 `OnGameLoadedLate`**：`ModHook.FireOnGameLoaded*` 只在
        ///   `PlayerStore.LoadGame()`（读档）里调（PlayerStore.cs），
        ///   新建游戏走的是 `NewGameData` → `FireOnNewGameStarted`（NewGameData.cs）。
        ///   所以这里必须把"进档才装"的那几样**全部再装一次**（全部幂等）：
        ///   延迟补丁（R1 / 倒水溢出 / 滤芯选择 / 电话名 / 存盘摘名片）、电话名片、扩容等级、服务注册。
        ///   少装补丁的表现（实机 2026-11-20 v0.10.0）：新开局电话簿里名片名字变成
        ///   `Translation Error 'megatank.phone' in ClientName`，且服务页没有那三条。
        ///
        /// 安全性：此时已在游戏内（主菜单之后），本地化已初始化，手动 patch 不会触发
        /// `Liquid..cctor` 的坏句柄问题；仍按 LatePatches 的纪律逐项 `Step` 兜底。
        /// </summary>
        internal static void OnNewGameStarted()
        {
            Step("本地化闸", Text.MarkReady);
            Step("补丁", LatePatches.Install);
            Step("新档电话名片", PhoneCard.Install);
            Step("新档容器索引", TankRegistry.RefreshFromStore);
            Step("新档水库状态", ReservoirRuntime.Clear);

            // 新档没有 ModData ⇒ 0 档（100 L）。先落一次口径标记，避免下次进档误当成老档，
            // 再把已生成的物品对齐。
            Step("新档扩容口径", Capacity.EnsureScheme);
            Step("新档扩容等级", () =>
            {
                Capacity.Invalidate();
                Capacity.ApplyAll();
            });

            Step("新档属性对齐", () => TankAttributes.ApplyAll());

            Step("新档服务注册", Services.EnsureRegistered);
        }

        private static void Step(string what, Action action)
        {
            if (action == null) return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("boot-" + what, what + " 安装失败（其余继续）：" + e.Message);
            }
        }
    }
}

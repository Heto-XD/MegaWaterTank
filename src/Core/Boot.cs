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

            // 扩容等级：先迁移老档口径（0.9.x → 0.10.0，等级 +8 保住容量），
            // 再清缓存，最后让老容器对齐当前档位。
            // 放在服务注册之前——服务注册要看 CanUpgrade 决定满级置灰。
            Step("扩容口径", Capacity.EnsureScheme);
            Step("扩容等级", () =>
            {
                Capacity.Invalidate();
                Capacity.ApplyAll();
            });

            Step("服务注册", Services.EnsureRegistered);
        }

        /// <summary>
        /// 新开局（NewGameData.FireOnNewGameStarted）。
        /// 新档只走这条，不走 OnGameLoadedLate——所以必须在这里补一次名片，
        /// 否则新档电话簿里没有水商。此时 PhoneClientDict 已在 StartNewGame() 里
        /// 用 InitPhoneClientDict() 建好（源码 NewGameData.cs:401 / PlayerStore.cs:822）。
        /// 补丁/服务在 OnLoadedLate 已装且幂等，新开局不必重装。
        /// </summary>
        internal static void OnNewGameStarted()
        {
            Step("本地化闸", Text.MarkReady);
            Step("新档电话名片", PhoneCard.Install);

            // 新档没有 ModData ⇒ 0 档（100 L）。先落一次口径标记，避免下次进档误当成老档，
            // 再把已生成的物品对齐。
            Step("新档扩容口径", Capacity.EnsureScheme);
            Step("新档扩容等级", () =>
            {
                Capacity.Invalidate();
                Capacity.ApplyAll();
            });
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

using System;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 入口：容器（主池 + 8 内胆槽）+ R1 溢出 + R2 回补 + 档图巡检 + tooltip。
    /// 电话订购与三语本地化在 P2。
    ///
    /// 本程序集**不含 [HarmonyPatch] 特性补丁**：其中两个目标（AddWater / tooltip）的方法体
    /// 都读 Liquid 静态字段，加载期 PatchAll 会触发 Liquid 的本地化 cctor 并把它打成永久损坏。
    /// 所以 4 个补丁由 LatePatches 在 OnGameLoadedLate（本地化就绪）之后手动安装；
    /// 第 5 个（配置面板语言）例外——它必须在 OnEnable 就装，因为主菜单面板进档前就能打开，
    /// 目标是纯静态类、无危险 cctor，见 src\Patches\ConfigSectionPatch.cs 头注释。
    ///
    /// 启停纪律（借 PowerManager LESSONS）：逐项 try + 幂等，绝不一个 try 包全部——
    /// OnDisable 只在强制停用/退出时跑，一处异常会让后面的清理全部跳过，且真机上几乎发现不了。
    /// </summary>
    public class ModEntry : IMod
    {
        public void Init(ModManifest manifest)
        {
            // ★ 配置必须在加载期定义：官方主菜单 Modding 面板用 ModConfig.Find(modId)
            //   判断"有没有设置项"，只有 GetConfig() 创建过实例才返回 true。
            //   放到进档后 ⇒ 主菜单齿轮置灰、点开提示 has no configuration entries。
            //   PowerManager / SentinelTracker 同样在 IMod.Init 里 Init 配置。
            //   安全性：section 是字面量，label/tooltip 是延迟 lambda，Init 期不读 locale。
            Step("日志开关配置", DebugConfig.Init);
            Debug.Log("[MegaTank] Init v" + Ids.Version);
        }

        public void OnEnable()
        {
            Fault.Clear();
            Step("订阅物品注册", () => ModHook.OnModItemDirectoryInit += TankItems.Register);
            Step("订阅载入完成", () => ModHook.OnGameLoadedLate += Boot.OnLoadedLate);
            Step("订阅新开局", () => ModHook.OnNewGameStarted += OnNewGameStartedWrap);
            Step("启动巡检", TankRuntime.EnsureTicker);

            // ★ 必须在加载期装：主菜单 → Modding → 齿轮就能打开配置面板，
            //   那时 OnGameLoadedLate 还没跑（实机 Player.log 实证：补丁 5/5 就绪在读档之后，
            //   而面板在那之前打开 ⇒ 分组名停在并排原样）。ModConfigWindow 无静态字段，
            //   提前 patch 不会触发危险 cctor；ContainerManager 2.16.0 同样在 OnEnable 装。
            Step("配置面板语言补丁", () => ConfigSectionPatch.Install());

            Debug.Log("[MegaTank] OnEnable 完成：补丁等待游戏载入完成后安装");
        }

        public void OnDisable()
        {
            Step("退订物品注册", () => ModHook.OnModItemDirectoryInit -= TankItems.Register);
            Step("退订载入完成", () => ModHook.OnGameLoadedLate -= Boot.OnLoadedLate);
            Step("退订新开局", () => ModHook.OnNewGameStarted -= OnNewGameStartedWrap);
            Step("撤销电话名片", PhoneCard.Uninstall);
            Step("撤销服务注册", Services.Unregister);
            Step("卸载配置面板补丁", ConfigSectionPatch.Uninstall);
            Step("卸载补丁", LatePatches.Uninstall);
            Step("停止巡检", TankRuntime.RemoveTicker);
            Step("清扩容缓存", Capacity.Invalidate);
            Step("关闭本地化闸", Text.ResetReady);
            Fault.Clear();

            Debug.Log("[MegaTank] OnDisable 完成");
        }

        /// <summary>OnNewGameStarted 是 Action&lt;string&gt;（带 scenarioId），这里适配并兜底。</summary>
        private static void OnNewGameStartedWrap(string scenarioId)
        {
            try { Boot.OnNewGameStarted(); }
            catch (Exception e)
            {
                Debug.LogWarning("[MegaTank] 新开局编排失败：" + e.Message);
            }
        }

        /// <summary>逐项兜底：一步失败不影响后续清理（LESSONS：一个 try 包全部是沉默的债）。</summary>
        private static void Step(string what, Action action)
        {
            if (action == null) return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MegaTank] " + what + " 失败：" + e.Message);
            }
        }
    }
}

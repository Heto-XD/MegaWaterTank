using System;

namespace MegaTank
{
    /// <summary>
    /// 日志开关（官方 ModHelper.GetConfig；值持久化到 ModConfigs/megatank.json）。
    ///
    /// 三条纪律（借工作区既有事故，别再踩）：
    ///   1. **必须在 IMod.Init（加载期）定义**：官方主菜单 Modding 面板用
    ///      `ModConfig.Find(modId)` 判断有没有设置项，只有 `GetConfig()` 创建过实例才为 true。
    ///      放到进档后 ⇒ 主菜单齿轮置灰、点开提示 has no configuration entries（0.8.0 首版踩过）。
    ///      PowerManager / SentinelTracker 同样在 IMod.Init 里 Init 配置。
    ///   2. **section 必须是编译期字面量**：Define 的 section 参数在 Init 期就要给，若走 Text.T
    ///      会在启动阶段读 locale，把游戏本地化初始化搅坏并连累同一次启动里的其它 mod。
    ///      label / tooltip 是 Func&lt;string&gt;（面板打开时才调），用 lambda 包 Text.T；
    ///      Text.T 自带 Ready 闸，未就绪返回英文，安全。
    ///   3. **section 的按语言切换在面板打开时做**（0.8.2）：Init 期给「English / 中文」并排，
    ///      ConfigSectionPatch 在 ModConfigWindow.Open 之前调 CfgLocale.ApplyTo 改写成单语。
    ///      走官方 ModReflect.Set（字面成员名，不触发 reflection-external 扫描），
    ///      改的是 Section 这一运行时属性，**不进配置 JSON**，不污染玩家存档/配置。
    ///      0.8.1 及以前的"固定英文分组名"由此作废。
    /// </summary>
    internal static class DebugConfig
    {
        // Init 期必须给"编译期就存在"的字符串：这里返回「Diagnostics / 诊断」并排字面量
        // （CfgLocale.Sec 只做字符串拼接 + 登记词条键，**不读 locale**，守 A3 / R-17）。
        // 真正按语言切成单语发生在面板打开时——ConfigSectionPatch → CfgLocale.ApplyTo。
        private static readonly string Section = CfgLocale.Sec("megatank.cfg.sec.diagnostics", "Diagnostics", "诊断");
        private const string Key = "VerboseLog";

        private static ConfigEntry<bool> _verbose;
        private static bool _initialized;

        /// <summary>进档后调用，幂等。配置只定义一次，避免重复注册/重复订阅。</summary>
        internal static void Init()
        {
            if (_initialized) return;

            try
            {
                var cfg = ModHelper.GetConfig(Ids.ModId);
                if (cfg == null) return;

                _verbose = cfg.Define(
                    Key,
                    false,                              // 默认关：正常游玩不刷日志
                    Section,
                    () => Text.T("megatank.cfg.verbose", "详细日志", "Verbose log"),
                    tooltip: () => Text.T("megatank.cfg.verbose.tip",
                        "默认关 · 开启后输出初始化、补丁安装、物品注册等诊断日志；排障用",
                        "Off by default · Log init, patch install and item registration. For troubleshooting."));

                _verbose.OnChanged += OnVerboseChanged;
                Log.SetVerbose(_verbose.Value);

                // 机器槽位：现在完全跟随原版（按物品尺寸放大 + 居中），不再需要任何补丁。
                // 0.6.1~0.10.1 曾把槽位钉回 1×1 来压窗口变形，真根因（shape 被污染）在 0.8.3 已修；
                // 0.10.2 起恢复原版行为，0.10.3 移除补丁与配置项。

                _initialized = true;

                Log.Info("日志开关已加载：VerboseLog=" + _verbose.Value);
            }
            catch (Exception e)
            {
                // 配置系统不可用时不能影响 mod 主体功能，保持默认静默
                Log.Once("cfg-init", "日志开关初始化失败（按默认关闭）：" + e.Message);
            }
        }

        /// <summary>当前日志开关状态（诊断补丁等模块按它决定是否打印）。</summary>
        internal static bool VerboseLog => _verbose != null && _verbose.Value;

        private static void OnVerboseChanged(bool value)
        {
            Log.SetVerbose(value);
            // 这条用 Info：关掉时会看不到（符合预期），打开时能看到确认
            Log.Info("VerboseLog=" + value);
        }
    }
}

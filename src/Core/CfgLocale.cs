using System;
using System.Collections.Generic;

namespace MegaTank
{
    /// <summary>
    /// 配置面板分组名的本地化（借用 PowerManager / ContainerManager 的成熟做法）。
    ///
    /// 为什么不能简单地在 Define 里写 Text.T(...)：
    ///   官方 `ConfigEntry.Section` 是 **string**，在 `IMod.Init`（加载期）就求值定死；
    ///   此刻 Addressables 还没初始化，读 locale 会触发 game-kb MOD-019 的本地化链损坏。
    ///   所以 Init 期只能给**编译期拼好的并排字面量**（"Diagnostics / 诊断"），
    ///   任何字体至少能渲染英文那半，绝不隐形。
    ///
    /// 为什么打开面板时再改写：
    ///   `ModConfigWindow.Open` 里先取 `config.Sections`（遍历分组），再用
    ///   `entry.Section == section` 做条目归属比较——两个读取点必须看到同一个值，
    ///   所以在 Open 之前一次性改写单一数据源 `entry.Section`（不是补 getter，避免内联不一致）。
    ///   Section **不进**配置 JSON（ModConfig.ConfigFile 只存 key/value）⇒ 改写不污染玩家配置。
    ///
    /// 纪律：
    ///   - 只改本 mod（Ids.ModId）的条目，别的 mod 原样放行。
    ///   - 走 `ModReflect.Set`（官方免能力反射，字面成员名，不触发 reflection-external 扫描）。
    ///   - 词条缺失 ⇒ 保持并排原样（可见 > 裸 key / 空白），任何异常都不冒进官方面板。
    /// </summary>
    internal static class CfgLocale
    {
        /// <summary>Open 期登记的「并排串 → 词条键」；原始串也在这里，换语言时重新翻译。</summary>
        private static readonly Dictionary<string, string> SectionKeys =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>initWarned：Section 登记只在加载期发生一次，异常不必每帧刷。</summary>
        private static bool _rewriteWarned;

        /// <summary>
        /// Init 期取分组名：返回「English / 中文」并排字面量并登记词条键。
        /// **不读 locale**——参数是编译期常量，加载期调用安全（见 A3 / R-17）。
        /// </summary>
        internal static string Sec(string key, string en, string zh)
        {
            string text = en + " / " + zh;
            if (!string.IsNullOrEmpty(key)) SectionKeys[text] = key;
            return text;
        }

        /// <summary>
        /// 面板打开前调用：把本 mod 各条目的 Section 改成「当前语言的单语分组名」。
        /// 每次打开都算一次 ⇒ 主菜单里实时切换语言后立刻跟着变，不需要语言事件。
        /// </summary>
        internal static void ApplyTo(string modId)
        {
            if (string.IsNullOrEmpty(modId) || !string.Equals(modId, Ids.ModId, StringComparison.Ordinal)) return;

            try
            {
                var cfg = global::ModConfig.Find(modId);
                if (cfg == null || !cfg.HasEntries) return;

                foreach (global::ConfigEntry entry in cfg.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Section)) continue;

                    string key;
                    if (!SectionKeys.TryGetValue(entry.Section, out key)) continue;

                    // T(key, zh, en) 自带 Ready 闸：未就绪返回 en（拉丁可见），不会读坏本地化
                    string text = Text.T(key, null, null);
                    // 词条缺失时 T 返回 key 本身 ⇒ 保持并排原样（可见 > 隐形/裸 key）
                    if (string.IsNullOrEmpty(text) || string.Equals(text, key, StringComparison.Ordinal))
                        continue;

                    if (string.Equals(entry.Section, text, StringComparison.Ordinal)) continue;

                    global::ModReflect.Set(entry, "Section", text);
                    // 改写后登记新串，下次切语言时还能从词条键重译
                    SectionKeys[text] = key;
                }
            }
            catch (Exception e)
            {
                // 绝不冒进官方面板：分组名停留在并排原样（数据源仍一致，不会出现空组）
                if (_rewriteWarned) return;
                _rewriteWarned = true;
                Log.Once("cfg-section", "配置分组名改写失败（保持「English / 中文」原样）：" + e.Message);
            }
        }
    }
}

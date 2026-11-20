using System;

namespace MegaTank
{
    /// <summary>
    /// 本地化门面：优先官方表（`Localization/*.csv` → `ModHelper.GetLocalized`），
    /// 表未就绪或键缺失时按语言给**内联兜底**。
    ///
    /// 三条纪律（借 PowerManager Text.cs / 项目规范 §4.5）：
    ///   1. 非中文语言绝不返回中文——那些语言的字体没有 CJK 字形，中文整段隐形。
    ///   2. 官方加载器已实现「当前语言 → en → key」的回退，所以不需要自建覆盖层；
    ///      我们只额外补一个"表还没加载"的兜底。
    ///   3. 语言探测会读 `LocalizationSettings.SelectedLocale`，**只能在进档后调用**；
    ///      本类的方法只被物品工厂 / tooltip / 服务注册调用（全部在 OnGameLoadedLate 之后）。
    /// </summary>
    internal static class Text
    {
        /// <summary>
        /// 本地化就绪闸（game-kb `MOD-019`）：`Init`/`OnEnable`/目录初始化期**绝不读 locale**——
        /// `LocHelper.GetCurrentLocaleCode()` 会碰 `LocalizationSettings.SelectedLocale`，
        /// 而 Addressables 未初始化时读取会把整条初始化链弄成 invalid handle，连坐其它 mod。
        /// 未就绪一律返回英文（拉丁字符可见），就绪后立刻恢复中文。
        /// </summary>
        internal static bool Ready;

        internal static void MarkReady() { Ready = true; }
        internal static void ResetReady() { Ready = false; }

        /// <summary>取文案：官方表 → （zh 环境：中文兜底；其余：英文兜底）。</summary>
        internal static string T(string key, string zh, string en)
        {
            if (!Ready) return en;   // 就绪前不碰本地化表，也不读 locale

            try
            {
                string value = ModHelper.GetLocalized(Ids.ModId, key);
                if (!string.IsNullOrEmpty(value) && !string.Equals(value, key, StringComparison.Ordinal))
                    return value;
            }
            catch (Exception e)
            {
                Log.Once("loc-" + key, "读本地化失败（用内联兜底）：" + e.Message);
            }

            return IsChinese() ? zh : en;
        }

        /// <summary>带格式化参数的版本（表里用 {0} 占位）。</summary>
        internal static string T(string key, string zh, string en, params object[] args)
        {
            string text = T(key, zh, en);
            if (args == null || args.Length == 0) return text;

            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                Log.Once("loc-format-" + key, "本地化条目不是合法格式串：" + key);
                return text;
            }
        }

        /// <summary>当前是不是中文环境；读不到一律按"非中文"处理（英文可见，中文会隐形）。</summary>
        internal static bool IsChinese()
        {
            try
            {
                string code = LocHelper.GetCurrentLocaleCode();
                return !string.IsNullOrEmpty(code) && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Log.Once("loc-code", "读语言代码失败（按非中文处理）：" + e.Message);
                return false;
            }
        }
    }
}

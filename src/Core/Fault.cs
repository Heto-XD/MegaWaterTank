using System;

namespace MegaTank
{
    /// <summary>
    /// 故障位：判据/流程出错时记一笔，由调用方一次性消费并走"安全侧"。
    ///
    /// 为什么要这一层（借 ItemStacking 的 JudgeFaults 口径）：
    ///   R1/R2 在 0.25 s 循环和灌水热点里跑，出错就用 Log.Once 打一条是不够的——
    ///   调用方需要知道"刚才那次失败是异常，不是真的没水"，否则会把故障当成正常状态。
    ///   约定：**异常 → 置位 → 本次跳过自己的附加逻辑（原版流程照常）→ 上层消费一次并提示**。
    /// </summary>
    internal static class Fault
    {
        /// <summary>数据保护类：涉及水位/容量/存档，出错时必须放弃本次写入。</summary>
        internal const int Protection = 1;

        /// <summary>便利类：显示/贴图，出错只影响观感。</summary>
        internal const int Cosmetic = 2;

        private static int _bits;

        internal static void Mark(int bit)
        {
            _bits |= bit;
        }

        /// <summary>取走一次故障标记（返回 true 表示"上次确实出过错"）。</summary>
        internal static bool Consume(int bit)
        {
            bool hit = (_bits & bit) != 0;
            if (hit) _bits &= ~bit;
            return hit;
        }

        internal static void Clear()
        {
            _bits = 0;
        }

        /// <summary>
        /// 统一兜底：跑一段可能抛的代码，异常时置位 + 只报首次 + 返回 false。
        /// 不吞 OutOfMemory / StackOverflow —— 那两种继续跑没有意义。
        /// </summary>
        internal static bool Run(string key, int bit, Action action, string what)
        {
            if (action == null) return false;
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                if (e is OutOfMemoryException || e is StackOverflowException) throw;
                Mark(bit);
                Log.Once(key, what + " 失败（本次跳过）：" + e.Message);
                return false;
            }
        }
    }
}

namespace MegaTank
{
    /// <summary>
    /// 纯算术核：不摸任何 Unity / 游戏类型，可离线复算（tests\PureCheck 直接链接本文件）。
    ///
    /// 这里的每条公式都在实机路径上，改错会静默变形（档位跳档、溢出量算少/multi），
    /// 所以刻意抽出来做成可测函数。
    /// </summary>
    internal static class WaterMath
    {
        internal static int Clamp(int value, int lo, int hi)
        {
            if (value < lo) return lo;
            if (value > hi) return hi;
            return value;
        }

        /// <summary>水位百分比（0..100）→ 档位（0..maxBucket）。</summary>
        internal static int Bucket(int percent, int maxBucket)
        {
            return Clamp(percent / 10, 0, maxBucket);
        }

        /// <summary>
        /// R1 溢出量 = 请求量 − 实际进入量。
        /// 实际进入量用「灌水前剩余容量 − 灌水后剩余容量」反算，因为 AddWater 不返回实际值。
        /// 负数一律归零（AddWater 只会让剩余容量变小或不变）。
        /// </summary>
        internal static int Overflow(int requested, int freeBefore, int freeAfter)
        {
            int actual = freeBefore - freeAfter;
            if (actual < 0) return 0;          // 剩余容量变大：数据异常，不猜
            int overflow = requested - actual;
            return overflow > 0 ? overflow : 0;
        }

        /// <summary>
        /// 本次到底该不该溢出。
        ///
        /// ★ 关键保护：本来有余量（freeBefore &gt; 0）却一滴没进（actual == 0），
        ///   说明 AddWater 提前返回了（例如容器临时丢了 LIQUID_CONTAINER_TAG，或别的 mod 拦了）——
        ///   这时若按"请求量 − 实际"算溢出，会把整笔请求当成溢出灌进内胆，等于**凭空造水**。
        ///   只有"主池本来就满"（freeBefore == 0）才允许 actual == 0 的正常溢出。
        /// </summary>
        internal static bool ShouldSpill(int requested, int freeBefore, int freeAfter)
        {
            if (requested <= 0) return false;

            int actual = freeBefore - freeAfter;
            if (actual < 0) return false;                      // 剩余容量变大：数据异常，不猜
            if (actual == 0 && freeBefore > 0) return false;    // 有余量却没进水：不是真实灌水
            return true;
        }

        /// <summary>本次往内胆灌多少：不超过它的剩余容量。</summary>
        internal static int SpillAdd(int remaining, int free)
        {
            if (remaining <= 0 || free <= 0) return 0;
            return remaining < free ? remaining : free;
        }

        /// <summary>是否需要回补：主池水位低于阈值（严格小于）。</summary>
        internal static bool NeedsRefill(int percent, int threshold)
        {
            return percent < threshold;
        }
    }
}

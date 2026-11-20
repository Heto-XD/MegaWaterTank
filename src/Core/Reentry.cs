// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// 重入闸（借 PowerManager 的 ReentryGate 口径，只保留实例级深度判定）。
    ///
    /// 为什么需要：R2 回补走 WaterHelper.TransferLiquid，而 TransferLiquid 会被其它 mod 补
    /// （ItemStacking 就补了）。一旦对方在我们回补期间又调回 AddWater/TransferLiquid，
    /// 就会形成「补丁触发补丁」的同步递归——栈溢出之前不会有任何日志。
    ///
    /// 线程模型：Unity 主线程 + 过夜结算都是同一线程，用 [ThreadStatic] 足够；
    /// 真出现跨线程调用时，每个线程各自一套深度，最坏是少吃一层保护，不会误判成死锁。
    /// </summary>
    internal static class Reentry
    {
        /// <summary>正常业务最多 2 层（回补→转移→单件），超过 4 层必是自激。</summary>
        private const int MaxDepth = 4;

        [ThreadStatic] private static int _depth;

        internal static bool Busy => _depth > 0;

        /// <summary>进闸。false = 已超限，调用方应"不干预"直接返回，不要抛。</summary>
        internal static bool TryEnter()
        {
            if (_depth >= MaxDepth)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("reentry", "重入深度超限（>" + MaxDepth + "），本次跳过以避免递归");
                return false;
            }

            _depth++;
            return true;
        }

        internal static void Exit()
        {
            if (_depth > 0) _depth--;
        }
    }
}

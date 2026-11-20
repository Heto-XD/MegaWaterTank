using System.Collections.Generic;
using UnityEngine;

namespace MegaTank
{
    internal static class Log
    {
        private static readonly HashSet<string> Seen = new HashSet<string>();

        /// <summary>由 DebugConfig（官方配置 VerboseLog）注入；默认 false = 正常运行不刷日志。</summary>
        private static bool _verbose;

        internal static void SetVerbose(bool value) { _verbose = value; }

        /// <summary>
        /// 信息级日志：只有 VerboseLog=true 才输出。
        /// 初始化完成、补丁安装、物品注册、贴图校准这类"正常时无需看"的都走这里。
        /// </summary>
        internal static void Info(string msg)
        {
            if (!_verbose) return;
            Debug.Log("[MegaTank] " + msg);
        }

        /// <summary>
        /// 同一个 key 只报一次——巡检类循环里防止每秒刷屏。
        /// ★ 警告/故障级日志**不受开关控制**：排障开关不能把异常静默掉。
        /// 这类信息量很小（每类问题一条），不构成日志噪音。
        /// </summary>
        internal static void Once(string key, string msg)
        {
            if (Seen.Add(key)) Debug.LogWarning("[MegaTank] " + msg);
        }
    }
}

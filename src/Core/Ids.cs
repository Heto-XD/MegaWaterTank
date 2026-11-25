// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace MegaTank
{
    /// <summary>全局常量。改容量/槽位只动这里。</summary>
    internal static class Ids
    {
        internal const string ModId = "megatank";
        internal const string Version = "0.16.0";

        internal const string Tank = "mega_water_tank";
        internal const string Cell = "mega_water_cell";

        internal const string TankTag = "MEGA_TANK_TAG";
        internal const string CellTag = "MEGA_CELL_TAG";

        // ── 容量（0.11.4 起：100 L 起步，逐档 +100 L，0.13.0 起满级 2000 L）──
        // 主池与每个内胆共用同一容量；扩容等级是**存档级全局**（ModData），
        // 买一次所有容器同时变大。满配：2000 L × 25 个容器（主池 + 24 内胆）= 50.0 吨。
        // 注意上限：WaterHelper.InitLiquidContainerItem 对 >2147483 ml 会报错并夹取，
        // 我们的 2000 L 仍低于它；part 值 = ml × 1000 = 2.0e9，仍在 int 内（约 2.1e9 顶）。

        /// <summary>起步容量 100 L（0.11.4 起；0.9.x 是 900 L，老档由 <c>Capacity.EnsureScheme</c> 迁过来）。</summary>
        internal const int BaseCapacityMl = 100000;

        /// <summary>0.9.x 的起步容量，仅用于把老档等级换算到新口径。</summary>
        internal const int LegacyBaseCapacityMl = 900000;

        /// <summary>每档扩容 +100 L。</summary>
        internal const int ExpandStepMl = 100000;

        /// <summary>
        /// 老档等级补偿：旧公式 900 + 100×L 换算到新公式 100 + 100×(L+8)。
        /// = (900 − 100) / 100 = 8 档。迁移后老档容量**一点不变**。
        /// </summary>
        internal const int LegacyLevelOffset = (LegacyBaseCapacityMl - BaseCapacityMl) / ExpandStepMl;

        /// <summary>扩容上限 19 档（100 + 19×100 = 2000 L；单容器 int 硬上限 2147.48 L）。</summary>
        internal const int MaxExpandLevel = 19;

        /// <summary>满级容量 = 2000 L（与旧版常量同名保留，避免漏改）。</summary>
        internal const int CapacityMl = BaseCapacityMl + MaxExpandLevel * ExpandStepMl;

        // ── R3 机器预腾（0.14.0：配置化 + 夜间预腾/回填）──
        /// <summary>只对湿气农场生效；R3 不碰其它机器槽。</summary>
        internal const string MoistureFarmId = "moisture_farm";

        /// <summary>默认预留上限 25 L；玩家可在配置里 0–50 L 调整，0 = 关闭。</summary>
        internal const int DefaultReserveLiters = 25;

        /// <summary>配置允许的最大预留值（升）。</summary>
        internal const int ReserveMaxLiters = 50;

        /// <summary>自动模式的安全余量：0.5 L = 500,000 part。</summary>
        internal const int ReserveMarginParts = 500_000;

        /// <summary>
        /// 连续模式门槛：实际 reserve 占容量 ≤ 8% 时才每 tick 巡检。
        /// 这样主池稳定在 ≥92%，与 R2 的 90% 回补阈值留出死区；超过门槛只在夜间一次性预腾。
        /// </summary>
        internal const int ReserveContinuousMaxPercent = 8;

        /// <summary>否定结果缓存秒数（不在机器槽/没电时，避免每 tick 查父链）。</summary>
        internal const float ReserveNegativeCacheSeconds = 2f;

        /// <summary>机器槽目标缓存秒数（连续模式下避免每 tick 重算当夜产量）。</summary>
        internal const float ReservePositiveCacheSeconds = 5f;

        // ── 水库模式（0.15.0 Phase 2）──
        /// <summary>默认目标水位 90%（沿用“主池基本满”的习惯）；玩家可改 0–100。</summary>
        internal const int DefaultReservoirTargetPercent = 90;

        /// <summary>水库目标允许范围：0 = 主池清空，100 = 主池装满。</summary>
        internal const int ReservoirMinPercent = 0;
        internal const int ReservoirMaxPercent = 100;

        /// <summary>死区：目标 ±5% 内不搬水，避免每 tick 抖动。</summary>
        internal const int ReservoirBandPercent = 5;

        /// <summary>水库每次最多搬 25 L（25,000,000 part），防止单帧大搬。</summary>
        internal const int ReservoirMaxMoveParts = 25_000_000;

        /// <summary>水库搬不动（无可用内胆/内胆满）后的重试间隔，避免每 tick 重复分配。</summary>
        internal const float ReservoirRetrySeconds = 5f;

        /// <summary>水库搬水汇总日志的最长间隔（秒）；中间 25 L 批次不逐条输出。</summary>
        internal const float ReservoirLogSeconds = 5f;

        // ── 容器基础价（0.12.0，对齐原版液体容器）──
        // 原版 `WaterTankHelper` 的水箱 = 350；`AmenitiesItemDirectory#WaterPitcher` 的滤水壶 = 25。
        // 空容器价值 = unitValue + 这个 tag；满水时 unitValue 会被原版改写成 40/升。
        // 只影响估价/展示，不改变 mod 的购买价（500 / 250 / 150）。

        /// <summary>主池容器基础价（原版 water_tank = 350）。</summary>
        internal const int TankBasePrice = 350;

        /// <summary>内胆容器基础价（对齐原版 water_pitcher = 25）。</summary>
        internal const int CellBasePrice = 25;

        /// <summary>按扩容等级算容量；超界自动夹到 [0, MaxExpandLevel]。</summary>
        internal static int CapacityAt(int level)
        {
            if (level < 0) level = 0;
            if (level > MaxExpandLevel) level = MaxExpandLevel;
            return BaseCapacityMl + level * ExpandStepMl;
        }

        /// <summary>内胆槽：4×6 = 24 个（0.15.0 Phase 1.1）。</summary>
        internal const int SlotsW = 4;
        internal const int SlotsH = 6;

        /// <summary>占格：主池 2×3，内胆 1×1（gridSize 恒 16）。</summary>
        internal const int TankW = 2;
        internal const int TankH = 3;
        internal const int CellW = 1;
        internal const int CellH = 1;

        /// <summary>R2 回补阈值：主池低于此百分比就补。</summary>
        internal const int RefillThreshold = 90;

        /// <summary>巡检周期（秒）：回补 + 档图同步都在这个节奏上。</summary>
        internal const float TickSeconds = 0.25f;

        /// <summary>
        /// 容器索引刷新周期（秒）：每 tick 只遍历本 mod 容器；这个周期用来兜住
        /// “玩家从背包/别的容器把水箱搬进店里”的情况，不是每 tick 全量扫。
        /// </summary>
        internal const float RegistryRefreshSeconds = 5f;

        /// <summary>水位档数：0..10，共 11 张图。</summary>
        internal const int MaxBucket = 10;
    }
}

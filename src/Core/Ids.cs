namespace MegaTank
{
    /// <summary>全局常量。改容量/槽位只动这里。</summary>
    internal static class Ids
    {
        internal const string ModId = "megatank";
        internal const string Version = "0.9.5";

        internal const string Tank = "mega_water_tank";
        internal const string Cell = "mega_water_cell";

        internal const string TankTag = "MEGA_TANK_TAG";
        internal const string CellTag = "MEGA_CELL_TAG";

        // ── 容量（0.9.0 起改为"起步 + 扩容"）──
        // 主池与每个内胆共用同一容量；扩容等级是**存档级全局**（ModData），
        // 买一次所有容器同时变大。满配：1900 L × 17 个容器 = 32.3 吨（与原设计一致）。
        // 注意上限：WaterHelper.InitLiquidContainerItem 对 >2147483 ml 会报错并夹取，
        // 我们的 1900 L 远低于它；part 值 = ml × 1000 = 1.9e9，仍在 int 内（约 2.1e9 顶）。

        /// <summary>起步容量 900 L。</summary>
        internal const int BaseCapacityMl = 900000;

        /// <summary>每档扩容 +100 L。</summary>
        internal const int ExpandStepMl = 100000;

        /// <summary>扩容上限 10 档（900 + 10×100 = 1900 L）。</summary>
        internal const int MaxExpandLevel = 10;

        /// <summary>满级容量 = 1900 L（与旧版常量同名保留，避免漏改）。</summary>
        internal const int CapacityMl = BaseCapacityMl + MaxExpandLevel * ExpandStepMl;

        /// <summary>按扩容等级算容量；超界自动夹到 [0, MaxExpandLevel]。</summary>
        internal static int CapacityAt(int level)
        {
            if (level < 0) level = 0;
            if (level > MaxExpandLevel) level = MaxExpandLevel;
            return BaseCapacityMl + level * ExpandStepMl;
        }

        /// <summary>内胆槽：4×4 = 16 个。</summary>
        internal const int SlotsW = 4;
        internal const int SlotsH = 4;

        /// <summary>占格：主池 2×3，内胆 1×1（gridSize 恒 16）。</summary>
        internal const int TankW = 2;
        internal const int TankH = 3;
        internal const int CellW = 1;
        internal const int CellH = 1;

        /// <summary>R2 回补阈值：主池低于此百分比就补。</summary>
        internal const int RefillThreshold = 90;

        /// <summary>巡检周期（秒）：回补 + 档图同步都在这个节奏上。</summary>
        internal const float TickSeconds = 0.25f;

        /// <summary>水位档数：0..10，共 11 张图。</summary>
        internal const int MaxBucket = 10;
    }
}

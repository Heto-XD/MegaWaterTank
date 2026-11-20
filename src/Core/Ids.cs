namespace MegaTank
{
    /// <summary>全局常量。改容量/槽位只动这里。</summary>
    internal static class Ids
    {
        internal const string ModId = "megatank";
        internal const string Version = "0.8.3";

        internal const string Tank = "mega_water_tank";
        internal const string Cell = "mega_water_cell";

        internal const string TankTag = "MEGA_TANK_TAG";
        internal const string CellTag = "MEGA_CELL_TAG";

        /// <summary>1,900,000 ml = 1.9e9 part，int 水位安全上限（留约 12% 余量）。</summary>
        internal const int CapacityMl = 1900000;

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

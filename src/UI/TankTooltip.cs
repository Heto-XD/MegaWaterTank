using System;
using System.Globalization;
using HarmonyLib;

namespace MegaTank
{
    /// <summary>
    /// tooltip：主池/内胆走自己的格式，其余容器原样交给原版。
    ///
    /// ⚠️ 同样不用 [HarmonyPatch] 特性：CreateWaterContainerTooltip 方法体读 Liquid 静态字段，
    ///   加载期 patch 会触发 Liquid 的本地化 cctor（见 OverflowPatch 注释）。
    ///
    /// 两个格式约束：
    ///   1. 不能用 " 1,872,403 ml / 1,900,000 ml" 这种纯 X ml / Y ml 格式——
    ///      ItemDetails 用正则 ^ (\d+) ml / (\d+) ml$ 改写原版这行，会二次处理。
    ///   2. 禁止 (float)：1.9e9 part 量级 float 精度只有 ±128 part，会显示抖动。
    /// </summary>
    internal static class TankTooltip
    {
        internal static bool Prefix(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || item == null) return true;
            if (!item.IsTag(Ids.TankTag) && !item.IsTag(Ids.CellTag)) return true;

            try
            {
                Write(builder, item);
            }
            catch (Exception e)
            {
                Log.Once("tooltip", "tooltip 构建失败，回退原版：" + e);
                return true;
            }
            return false;
        }

        private static void Write(RichTextBuilder builder, GameItem item)
        {
            int total = WaterHelper.GetTotalVolume(item);
            int cap = WaterHelper.GetTotalCapacity(item);
            int purity = WaterHelper.GetWaterPurity(item);

            // 单位只在行尾写一次：原来 "X ml / Y ml" 在 1,900,000 量级会换行，把 "ml" 挤到下一行。
            builder.AddLine(Num(total) + " / " + Num(cap) + " ml", ignoreEmpty: true);
            builder.AddLine(WaterFeatureHelper.GetWaterDisplayName(purity) + " · " + FormatPurity(purity), ignoreEmpty: true);
            builder.AddLine(Text.T("megatank.tooltip.hint",
                "成分请用水质检测仪查看", "Use a water scanner to inspect the contents"), ignoreEmpty: true);

            if (item.IsTag(Ids.TankTag))
            {
                int cells;
                long cellCap, cellWater;
                TankRuntime.CellStats(item, out cells, out cellCap, out cellWater);

                // 合计 = 主池 + 所有内胆的水量 / 容量；玩家不用逐个点开内胆就知道总上限
                if (cells > 0 && cellCap > 0)
                {
                    builder.AddLine(Text.T("megatank.tooltip.total",
                        "合计 {0} / {1} ml", "Total {0} / {1} ml",
                        FormatNum((long)total + cellWater), FormatNum((long)cap + cellCap)),
                        ignoreEmpty: true);
                }

                builder.AddLine(Text.T("megatank.tooltip.cells",
                    "内胆 {0} / {1}", "Cells {0} / {1}",
                    cells, Ids.SlotsW * Ids.SlotsH), ignoreEmpty: true);
            }
        }

        /// <summary>part → 可读 ml 数值（不带单位）。整数除 + 余数，绝不走 float。</summary>
        private static string Num(int part)
        {
            return FormatNum(part);
        }

        /// <summary>part → 数值文本（不带单位）。long 入参：8 个内胆合计会超 int。</summary>
        private static string FormatNum(long part)
        {
            if (part <= 0) return "0";

            long whole = part / 1000;
            long frac = part % 1000;

            if (whole >= 1000)
                return whole.ToString("N0", CultureInfo.InvariantCulture);

            if (whole >= 1)
                return whole + "." + (frac / 100).ToString(CultureInfo.InvariantCulture);

            return "0." + part.ToString("000", CultureInfo.InvariantCulture);
        }

        private static string FormatPurity(int purity)
        {
            if (purity <= 0) return "0.00%";
            return (purity / 100) + "." + (purity % 100).ToString("00", CultureInfo.InvariantCulture) + "%";
        }
    }
}

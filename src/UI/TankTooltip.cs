// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace MegaTank
{
    /// <summary>
    /// tooltip：只追加本 mod 独有的“合计 / 内胆”两行。
    ///
    /// 0.12.0 起不再抢 `WaterHelper#CreateWaterContainerTooltip`：
    ///   · 原版（或 ItemDetails 等接管同一条目的第三方 mod）继续负责水量、水质、
    ///     进度条和 `LAST_SCAN_RESULT_TAG` 扫描结果；
    ///   · 这样不会丢掉扫描行，也不会在未检测时暴露真实纯度；
    ///   · 不再需要 `Priority.First` 和 ItemDetails 抢同一个 Prefix。
    ///
    /// 用法：订阅官方 `ModHook.OnCreateTooltipLate`（`GameItemElement#GetTooltipBasic`
    /// 末尾触发），对**所有**物品都会调用，所以第一行必须尽快过滤掉非本 mod 物品。
    /// </summary>
    internal static class TankTooltip
    {
        internal static void Append(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || item == null) return;

            bool tank, cell;
            try
            {
                tank = item.IsTag(Ids.TankTag);
                cell = item.IsTag(Ids.CellTag);
            }
            catch
            {
                return;
            }
            if (!tank && !cell) return;

            try
            {
                if (tank)
                {
                    int cells;
                    long cellCap, cellWater;
                    TankRuntime.CellStats(item, out cells, out cellCap, out cellWater);

                    // 合计 = 主池 + 所有内胆的水量 / 容量；玩家不用逐个点开内胆就知道总上限。
                    if (cells > 0 && cellCap > 0)
                    {
                        long totalWater = (long)WaterHelper.GetTotalVolume(item) + cellWater;
                        long totalCap = (long)WaterHelper.GetTotalCapacity(item) + cellCap;

                        builder.AddLine(Text.T("megatank.tooltip.total",
                            "合计 {0} / {1} ml", "Total {0} / {1} ml",
                            FormatNum(totalWater), FormatNum(totalCap)),
                            ignoreEmpty: true);
                    }

                    builder.AddLine(Text.T("megatank.tooltip.cells",
                        "内胆 {0} / {1}", "Cells {0} / {1}",
                        cells, Ids.SlotsW * Ids.SlotsH), ignoreEmpty: true);
                }
            }
            catch (Exception e)
            {
                Log.Once("tooltip", "tooltip 追加失败（本次跳过）：" + e.Message);
            }
        }

        /// <summary>
        /// part → 数值文本（不带单位）。long 入参：17 个满级容器合计会超 int。
        /// 整数除 + 余数，绝不走 float（1.9e9 part 量级 float 精度只有 ±128 part）。
        /// </summary>
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
    }
}

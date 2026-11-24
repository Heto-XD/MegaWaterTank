// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// 精确搬水：从 source 搬 amount part 到 target，不多不少。
    ///
    /// 为什么不用 `WaterHelper.TransferLiquid`：
    ///   它是「搬到目标装满为止」——R3 只需要腾出固定余量，用它会一次把空内胆抽满。
    /// 为什么不用 `RemoveAndReturn` + `AddLiquid`：
    ///   `AddLiquid` 走默认 26 ℃ 的温度入口，热水/冷水温度会丢；这里按 TransferLiquid 的顺序
    ///   调 `ChemicalHelper.HandleReceiveLiquidTemp(target, part, source)`。
    ///
    /// 已知边界：只用于主池 ↔ 内胆，两者都不是 FILTER_CONTAINER_TAG，所以不复制
    /// TransferLiquid 的 HandleFilter 分支；若将来给它们加滤芯，这里必须补。
    /// </summary>
    internal static class LiquidMove
    {
        internal static int Move(GameItem source, GameItem target, int amount)
        {
            if (source == null || target == null || amount <= 0) return 0;
            if (!source.IsTag("LIQUID_CONTAINER_TAG") || !target.IsTag("LIQUID_CONTAINER_TAG")) return 0;

            int sourceTotal = WaterHelper.GetTotalVolume(source);
            int targetFree = WaterHelper.GetFreeCapacity(target);
            int moved = Math.Min(amount, Math.Min(sourceTotal, targetFree));
            if (moved <= 0) return 0;

            bool wasEmpty = WaterHelper.IsEmpty(target);
            int[] parts = WaterHelper.GetLiquidParts(source);
            int[] take = WaterHelper.SplitProportionally(parts, moved);

            for (int i = 0; i < take.Length; i++)
            {
                int part = take[i];
                if (part <= 0) continue;

                string tag = Liquid.Liquids[i].currentPart;
                int from = source.ReadTagInt(tag);
                int to = target.ReadTagInt(tag);
                if (part > from) part = from;
                if (part <= 0) continue;

                ChemicalHelper.HandleReceiveLiquidTemp(target, part, source);
                source.ModifyTag(tag, state => state.Enable().SetInt(from - part), skipValidate: true);
                target.ModifyTag(tag, state => state.Enable().SetInt(to + part), skipValidate: true);
            }

            WaterFeatureHelper.OnLiquidContainerPourInto(target, source, wasEmpty);
            WaterFeatureHelper.OnLiquidContainerPourOutOf(source);
            return moved;
        }
    }
}

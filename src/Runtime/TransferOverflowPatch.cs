// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// `WaterHelper.TransferLiquid` 的溢出口。
    ///
    /// 玩家用瓶子/水罐往水箱倒水、湿气农场用临时瓶往水箱倒水，走的都是 TransferLiquid，
    /// **不经过 AddWater**，所以 R1 的 AddWater 溢出补丁覆盖不到；主池满时水会留在源容器，
    /// 机器临时瓶随后还会被 Destroy。
    ///
    /// Postfix 只处理目标为巨型水箱的情况，把源里剩下的水按原成分精确搬进内胆；
    /// 不跳过原方法，不碰其它容器。
    /// </summary>
    internal static class TransferOverflowPatch
    {
        internal static void Postfix(GameItem sourceContainer, GameItem targetContainer)
        {
            try
            {
                if (sourceContainer == null || targetContainer == null) return;
                if (ReferenceEquals(sourceContainer, targetContainer)) return;
                if (!targetContainer.IsTag(Ids.TankTag)) return;
                if (WaterHelper.IsEmpty(sourceContainer)) return;

                int remaining = WaterHelper.GetTotalVolume(sourceContainer);
                if (remaining <= 0) return;

                int moved = TankRuntime.SpillSourceToCells(targetContainer, sourceContainer, remaining);
                if (moved < remaining)
                {
                    string id = sourceContainer.identifier ?? "?";
                    string fate = id.IndexOf("jug", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "临时瓶随后会被销毁，属于真实丢失"
                        : "水会留在源容器，不是丢失";

                    Log.Once("r1-transfer-lost-" + sourceContainer.GetUniqueID(),
                        "倒水溢出：源 " + id + "#" + sourceContainer.GetUniqueID()
                        + " 还有 " + (remaining - moved) + " part 没进内胆（目标 "
                        + targetContainer.identifier + "#" + targetContainer.GetUniqueID()
                        + "；内胆满/成堆/没有内胆；" + fate + "）");
                }
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("r1-transfer", "倒水溢出失败（本次余量留在源容器）：" + e.Message);
            }
        }
    }
}

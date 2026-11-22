// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 运行时：R2 回补 + 档图巡检 + R1 的目标选择。
    ///
    /// 四条纪律：
    ///   1. 不缓存 GameItem 引用——读档会重造所有物品，旧引用全是幽灵；
    ///      档位缓存用 uniqueId 作键。
    ///   2. 跳过正在被拖拽的主池——改 tag + 刷贴图会与拖拽的贴图通道打架。
    ///   3. 成堆（unitCount != 1）一律不动：液体是"整件绝对量"，一堆里所有件共享一份水，
    ///      且 ItemStacking 的 BlockFill 会拒绝任何对堆叠容器的装液。
    ///   4. 任何异常 → 故障位 + 只报首次；绝不让异常穿进 0.25 s 循环或过夜结算。
    /// </summary>
    internal static class TankRuntime
    {
        private static readonly Dictionary<int, string> StateCache = new Dictionary<int, string>();

        internal static void EnsureTicker()
        {
            TankRegistry.MarkDirty();
            FrameTicker.Ensure();
        }

        internal static void RemoveTicker()
        {
            FrameTicker.Remove();
            TankRegistry.Clear();
            StateCache.Clear();
        }

        internal static void Tick()
        {
            if (!Reentry.TryEnter()) return;
            try
            {
                TickCore();
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("tick", "Tick 异常（本 tick 跳过）：" + e.Message);
            }
            finally
            {
                Reentry.Exit();
            }
        }

        private static void TickCore()
        {
            var emp = EmporiumEntry.Instance;
            if (emp == null) return;

            // 0.11.6 P0：每 0.25 s 只遍历本 mod 的容器索引；
            // 全店 GetAllItems() 扫描降为每 RegistryRefreshSeconds 秒一次（兜底搬进店的容器）。
            bool refreshed = TankRegistry.EnsureFresh(Time.unscaledTime, Ids.RegistryRefreshSeconds);
            TankRegistry.Collect();
            List<GameItem> all = TankRegistry.Live;

            for (int i = 0; i < all.Count; i++)
            {
                GameItem item = all[i];
                if (item == null) continue;

                bool isTank = item.IsTag(Ids.TankTag);
                bool isCell = item.IsTag(Ids.CellTag);
                if (!isTank && !isCell) continue;

                if (IsStacked(item)) continue;

                bool dragging = IsDragging(item);

                // 旧存档兼容：spritePath 还是已删除的 _4x 键 → 强制重贴。
                // 放在巡检里兜底，避免依赖 onLoaded 的调用顺序（实机有漏网）。
                if (TankSprites.IsLegacySpritePath(item) && !dragging)
                {
                    TankSprites.ApplyBase(item);
                    Log.Once("legacy-sprite-" + item.GetUniqueID(),
                        "旧 spritePath 已重贴为当前基础图：" + item.identifier);
                }

                // 容量兜底不再每 tick 跑：进档/升级有 Capacity.ApplyAll；
                // 这里只跟着索引刷新周期做一次，省掉每 0.25 s 的 TagState 读取。
                if (refreshed && !dragging) Capacity.ApplyTo(item);

                // 水位百分比只读一次，回补和贴图共用（原来 SyncSprite 内会重复读两次）。
                int percent = WaterHelper.GetCurrentCapacity(item);
                if (isTank) Refill(item, percent, dragging);
                SyncSprite(item, percent);
            }

            // 电话名片由 OnGameLoadedLate + OnNewGameStarted 两个 hook 各装一次即可：
            // PhoneClientDict 是 ES3 存档字段，写进去就持久化；巡检每帧查字典是浪费。
            PruneCache();
        }

        /// <summary>成堆判定；命中时只提示一次（不再做任何状态改写）。</summary>
        private static bool IsStacked(GameItem item)
        {
            int units;
            try { units = item.unitCount; }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("units", "读 unitCount 失败（按单件处理）：" + e.Message);
                return false;
            }

            if (units <= 1) return false;

            Log.Once("stacked-" + item.identifier,
                item.identifier + " 处于堆叠状态（×" + units + "），已跳过回补与贴图；请拆成单件再使用");
            return true;
        }

        /// <summary>主池低于阈值时，从内胆按顺序回补。</summary>
        private static void Refill(GameItem tank, int percent, bool dragging)
        {
            if (dragging) return;

            if (!WaterMath.NeedsRefill(percent, Ids.RefillThreshold)) return;

            int stacked;
            List<GameItem> cells = Cells(tank, out stacked);
            for (int i = 0; i < cells.Count; i++)
            {
                GameItem cell = cells[i];
                if (cell == null || WaterHelper.IsEmpty(cell)) continue;

                try
                {
                    WaterHelper.TransferLiquid(cell, tank);
                }
                catch (Exception e)
                {
                    Fault.Mark(Fault.Protection);
                    Log.Once("refill", "回补失败（本次停止）：" + e.Message);
                    return;
                }

                if (WaterHelper.IsFull(tank)) return;
            }
        }

        /// <summary>
        /// 水质/水位变化时刷新贴图。缓存 (水位档, 平均色)，变了才调 item.Validate()——
        /// Validate 会重跑 <see cref="TankLiquidLayer.Overlays"/> 并应用 image.color。
        /// </summary>
        private static void SyncSprite(GameItem item, int percent)
        {
            int id = item.GetUniqueID();
            string key = percent <= 0
                ? "0"
                : WaterMath.Bucket(percent, Ids.MaxBucket)
                  + "|" + WaterHelper.GetLiquidContainerColor(item);

            string cached;
            if (StateCache.TryGetValue(id, out cached) && cached == key) return;

            if (!Fault.Run("sprite", Fault.Cosmetic, () => item.Validate(), "水质贴图刷新"))
                return;

            StateCache[id] = key;
        }

        /// <summary>
        /// R1：把溢出量用**同参 AddWater**灌进内胆（不能用 AddLiquid，会丢污染比例）。
        ///
        /// 重入：AddWater 会再次进本 mod 的 prefix/postfix（目标不是主池，Prefix 直接跳过），
        /// 但别的 mod 也可能在这条链上回调，所以外面套一层闸。
        /// </summary>
        internal static void SpillToCells(GameItem tank, int grade, int amount, bool useRange, int lowPara, int highPara)
        {
            if (amount <= 0) return;
            if (!Reentry.TryEnter()) return;

            try
            {
                SpillCore(tank, grade, amount, useRange, lowPara, highPara);
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("r1", "R1 溢出失败（本次丢弃余量）：" + e.Message);
            }
            finally
            {
                Reentry.Exit();
            }
        }

        private static void SpillCore(GameItem tank, int grade, int amount, bool useRange, int lowPara, int highPara)
        {
            int stacked;
            List<GameItem> cells = Cells(tank, out stacked);
            int remaining = amount;

            for (int i = 0; i < cells.Count && remaining > 0; i++)
            {
                GameItem cell = cells[i];
                if (cell == null) continue;

                int free = WaterHelper.GetFreeCapacity(cell);
                int toAdd = WaterMath.SpillAdd(remaining, free);
                if (toAdd <= 0) continue;

                int before = WaterHelper.GetTotalVolume(cell);
                WaterHelper.AddWater(cell, grade, toAdd, useRange, lowPara, highPara);
                int added = WaterHelper.GetTotalVolume(cell) - before;

                if (added <= 0) continue;   // 防御：没灌进去就换下一个，避免死循环
                remaining -= added;
            }

            if (remaining > 0) ReportSpillLost(cells.Count, stacked, remaining);
        }

        /// <summary>溢出没灌完时，把原因说清楚——否则玩家只看到"水凭空少了"。</summary>
        private static void ReportSpillLost(int cellCount, int stacked, int remaining)
        {
            if (cellCount == 0 && stacked == 0)
            {
                Log.Once("r1-no-cell", "R1：没有内胆，溢出 " + remaining + " part 被丢弃");
                return;
            }

            if (stacked > 0 && cellCount == 0)
            {
                // 最容易被误判成 bug 的一种：明明有内胆却灌不进去
                Log.Once("r1-stacked", "R1：槽里 " + stacked
                                           + " 个内胆都处于堆叠状态，成堆容器禁止装液，已跳过；溢出 "
                                           + remaining + " part 被丢弃（把小瓶拆成单件即可）");
                return;
            }

            Log.Once("r1-full", "R1：内胆已满或余量不足，溢出 " + remaining + " part 被丢弃");
        }

        /// <summary>
        /// 取槽内可用内胆（只走一层内容窗，不下钻其它物品、不去环、无递归）。
        /// <paramref name="stacked"/> 返回被跳过的成堆件数。
        /// </summary>
        private static List<GameItem> Cells(GameItem tank, out int stacked)
        {
            var result = new List<GameItem>();
            stacked = 0;

            List<GameItem> list = null;
            try
            {
                var window = tank.contentWindow;
                var inv = window != null ? window.child as GameInventory : null;
                list = inv != null ? inv.childItems : null;
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("cells", "读取主池内容窗失败（按无内胆处理）：" + e.Message);
                return result;
            }

            if (list == null) return result;

            for (int i = 0; i < list.Count; i++)
            {
                GameItem c = list[i];
                if (c == null || !c.IsTag(Ids.CellTag)) continue;

                if (c.unitCount != 1) { stacked++; continue; }
                result.Add(c);
            }

            return result;
        }

        /// <summary>
        /// 槽内统计：内胆个数、容量合计、水量合计。
        ///
        /// 用 long 累加：单件容量 1.9e9 part，8 件合计 1.52e10，int 会溢出成负数。
        /// 成堆的内胆按"占 1 格"计一次（一堆共享一份水，乘件数会算出假容量）。
        /// </summary>
        internal static void CellStats(GameItem tank, out int count, out long capacity, out long water)
        {
            count = 0;
            capacity = 0;
            water = 0;

            try
            {
                var window = tank != null ? tank.contentWindow : null;
                var inv = window != null ? window.child as GameInventory : null;
                var list = inv != null ? inv.childItems : null;
                if (list == null) return;

                for (int i = 0; i < list.Count; i++)
                {
                    GameItem c = list[i];
                    if (c == null || !c.IsTag(Ids.CellTag)) continue;

                    count++;
                    capacity += WaterHelper.GetTotalCapacity(c);   // long 累加
                    water += WaterHelper.GetTotalVolume(c);
                }
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("cell-stats", "统计内胆失败：" + e.Message);
            }
        }

        /// <summary>缓存键是 uniqueId，读档后 id 会变；长期游玩会积累，超过阈值整体清空。</summary>
        private static void PruneCache()
        {
            if (StateCache.Count > 512) StateCache.Clear();
        }

        private static bool IsDragging(GameItem item)
        {
            try
            {
                var handler = ItemMouseDragHandler.current;
                return handler != null && handler.GetCurrentItem() == item;
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("drag", "读拖拽状态失败（按未拖拽处理）：" + e.Message);
                return false;
            }
        }
    }
}

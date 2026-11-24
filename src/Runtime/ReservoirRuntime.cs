// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 水库模式：把主池维持在一个目标百分比附近，高于上限存入子胆、低于下限从子胆回补。
    ///
    /// 与 R2/R3 的归属：
    ///   · 水库模式开启时，TankRuntime 不再对水箱执行 R2（90% 回补）或 R3（机器预腾）；
    ///   · 主池↔子胆的精确搬水仍走 `LiquidMove.Move`，保留成分与温度；
    ///   · 过滤器只作用于进入主池的 AddWater/TransferLiquid，水库搬水不经过滤芯。
    ///
    /// 目标默认 90%，玩家可配 0–100%；死区固定 ±5%，每次最多搬 25 L。
    /// </summary>
    internal static class ReservoirRuntime
    {
        /// <summary>正在向目标水位搬运的状态：1 = 主池→子胆，-1 = 子胆→主池。key = uniqueId。</summary>
        private static readonly Dictionary<int, sbyte> Seeking = new Dictionary<int, sbyte>();

        /// <summary>搬不动后的重试时间；避免无可用内胆/内胆满时每 tick 重复分配。</summary>
        private static readonly Dictionary<int, float> RetryAfter = new Dictionary<int, float>();

        private struct MoveSummary
        {
            internal sbyte Direction;
            internal long Parts;
            internal float Started;
            internal string Identifier;
        }

        /// <summary>聚合 25 L/次的搬水日志：每次调平运行只输出一条汇总。</summary>
        private static readonly Dictionary<int, MoveSummary> Summaries = new Dictionary<int, MoveSummary>();

        internal static void Clear()
        {
            Seeking.Clear();
            RetryAfter.Clear();
            Summaries.Clear();
        }

        internal static bool Applies(GameItem tank)
        {
            return ReserveConfig.ReservoirMode
                   && tank != null
                   && tank.IsTag(Ids.TankTag);
        }

        internal static void Tick(GameItem tank)
        {
            try
            {
                if (!Applies(tank)) return;
                if (TankRuntime.IsStacked(tank)) return;
                if (TankRuntime.IsDragging(tank)) return;

                long cap = WaterHelper.GetTotalCapacity(tank);
                if (cap <= 1) return;

                long current = WaterHelper.GetTotalVolume(tank);
                long target = cap * ReserveConfig.ReservoirTargetPercent / 100;
                long band = cap * Ids.ReservoirBandPercent / 100;

                int id = tank.GetUniqueID();
                float now = Time.unscaledTime;
                if (RetryAfter.TryGetValue(id, out float retryAt) && now < retryAt) return;

                sbyte direction;
                if (!Seeking.TryGetValue(id, out direction) || direction == 0)
                {
                    if (current > target + band) direction = 1;
                    else if (current < target - band) direction = -1;
                    else return;

                    Seeking[id] = direction;
                }

                int moved;
                if (direction > 0)
                {
                    if (current <= target)
                    {
                        FlushMove(id);
                        Seeking.Remove(id);
                        RetryAfter.Remove(id);
                        return;
                    }

                    int amount = (int)Math.Min(current - target, int.MaxValue);
                    moved = MoveOut(tank, amount, Ids.ReservoirMaxMoveParts);
                }
                else
                {
                    if (current >= target)
                    {
                        FlushMove(id);
                        Seeking.Remove(id);
                        RetryAfter.Remove(id);
                        return;
                    }

                    int amount = (int)Math.Min(target - current, int.MaxValue);
                    moved = MoveIn(tank, amount, Ids.ReservoirMaxMoveParts);
                }

                if (moved <= 0)
                {
                    FlushMove(id);
                    Seeking.Remove(id);
                    RetryAfter[id] = now + Ids.ReservoirRetrySeconds;
                }
                else
                {
                    RecordMove(tank, direction, moved);
                    RetryAfter.Remove(id);
                    FlushIfOld(id, now);
                }

                if (Seeking.Count > 256) Seeking.Clear();
                if (RetryAfter.Count > 256) RetryAfter.Clear();
                if (Summaries.Count > 256) Summaries.Clear();
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("reservoir-tick", "水库调平失败（本 tick 跳过）：" + e.Message);
            }
        }

        /// <summary>
        /// 夜间兜底：如果刚开模式/刚读档主池仍高于目标，先压到目标，保证机器有空间。
        /// 只往子胆方向搬，不从子胆拉水（拉水会减少机器余量）。
        /// </summary>
        internal static void PrepareForNight(GameItem tank)
        {
            try
            {
                if (!Applies(tank)) return;
                if (TankRuntime.IsStacked(tank)) return;
                if (TankRuntime.IsDragging(tank)) return;

                long cap = WaterHelper.GetTotalCapacity(tank);
                if (cap <= 1) return;

                long current = WaterHelper.GetTotalVolume(tank);
                long target = cap * ReserveConfig.ReservoirTargetPercent / 100;
                if (current <= target) return;

                RetryAfter.Remove(tank.GetUniqueID());
                int amount = (int)Math.Min(current - target, int.MaxValue);
                int moved = MoveOut(tank, amount, int.MaxValue);
                if (moved > 0)
                {
                    RecordMove(tank, 1, moved);
                    FlushMove(tank.GetUniqueID());
                }
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("reservoir-night", "水库夜间预调失败（本次跳过）：" + e.Message);
            }
        }

        private static int MoveOut(GameItem tank, int amount, int maxMove)
        {
            if (amount <= 0) return 0;
            int stacked;
            List<GameItem> cells = TankRuntime.Cells(tank, out stacked);
            if (cells.Count == 0)
            {
                ReportNoCells(tank, stacked);
                return 0;
            }

            int remaining = Math.Min(amount, maxMove);
            int moved = 0;
            for (int i = 0; i < cells.Count && remaining > 0; i++)
            {
                GameItem cell = cells[i];
                if (cell == null) continue;

                int cellFree = WaterHelper.GetFreeCapacity(cell);
                int take = Math.Min(remaining, cellFree);
                if (take <= 0) continue;

                int done = LiquidMove.Move(tank, cell, take);
                moved += done;
                remaining -= done;
            }

            return moved;
        }

        private static int MoveIn(GameItem tank, int amount, int maxMove)
        {
            if (amount <= 0) return 0;
            int stacked;
            List<GameItem> cells = TankRuntime.Cells(tank, out stacked);
            if (cells.Count == 0)
            {
                ReportNoCells(tank, stacked);
                return 0;
            }

            int remaining = Math.Min(amount, maxMove);
            int moved = 0;
            for (int i = 0; i < cells.Count && remaining > 0; i++)
            {
                GameItem cell = cells[i];
                if (cell == null) continue;

                int cellWater = WaterHelper.GetTotalVolume(cell);
                int take = Math.Min(remaining, cellWater);
                if (take <= 0) continue;

                int done = LiquidMove.Move(cell, tank, take);
                moved += done;
                remaining -= done;
            }

            return moved;
        }

        private static void ReportNoCells(GameItem tank, int stacked)
        {
            int id = tank.GetUniqueID();
            if (stacked > 0)
            {
                Log.Once("reservoir-stacked-" + id,
                    "水库模式" + Stamp(tank) + "：内胆成堆（" + stacked + " 堆），水库/溢出会跳过；请拆成单件");
            }
            else
            {
                // 没有内胆是正常状态（只是水库无法调平），不当作 Warning。
                Log.Info("水库模式" + Stamp(tank) + "：没有内胆，跳过水库调平");
            }
        }

        private static void RecordMove(GameItem tank, sbyte direction, int moved)
        {
            if (moved <= 0) return;

            int id = tank.GetUniqueID();
            if (Summaries.TryGetValue(id, out MoveSummary summary) && summary.Direction == direction)
            {
                summary.Parts += moved;
                Summaries[id] = summary;
                return;
            }

            FlushMove(id);
            Summaries[id] = new MoveSummary
            {
                Direction = direction,
                Parts = moved,
                Started = Time.unscaledTime,
                Identifier = tank.identifier
            };
        }

        private static void FlushIfOld(int id, float now)
        {
            if (!Summaries.TryGetValue(id, out MoveSummary summary)) return;
            if (now - summary.Started < Ids.ReservoirLogSeconds) return;
            FlushMove(id);
        }

        private static void FlushMove(int id)
        {
            if (!Summaries.TryGetValue(id, out MoveSummary summary)) return;
            Summaries.Remove(id);
            if (summary.Parts <= 0) return;

            Log.Info("水库[" + summary.Identifier + "#" + id + "]："
                     + (summary.Direction > 0 ? "主池→子胆" : "子胆→主池")
                     + " 共 " + summary.Parts + " part（" + (summary.Parts / 1000) + " ml）");
        }

        private static string Stamp(GameItem item)
        {
            try { return "[" + item.identifier + "#" + item.GetUniqueID() + "]"; }
            catch { return "[" + item.identifier + "]"; }
        }
    }
}

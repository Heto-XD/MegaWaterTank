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

        /// <summary>R3 的目标缓存：key = uniqueId；Parts=0 表示「不是机器槽/机器没电」，正数表示本次预留量。</summary>
        private struct ReservePlan
        {
            internal GameInventory Inv;
            internal int Parts;
            internal float Until;
        }

        private static readonly Dictionary<int, ReservePlan> ReservePlans = new Dictionary<int, ReservePlan>();

        internal static void EnsureTicker()
        {
            ReservoirRuntime.Clear();
            TankRegistry.MarkDirty();
            FrameTicker.Ensure();
        }

        internal static void RemoveTicker()
        {
            FrameTicker.Remove();
            TankRegistry.Clear();
            StateCache.Clear();
            ReservePlans.Clear();
            ReservoirRuntime.Clear();
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
                // 同一周期顺带校正 0.12.0 的原版属性（基础价 / NOT_FOR_RESALE / 旧容器 tag），
                // 兜住“玩家从背包把老档水箱搬进店里”的情况。
                if (refreshed && !dragging)
                {
                    Capacity.ApplyTo(item);
                    TankAttributes.Ensure(item);
                }

                // 水位百分比只读一次；水库模式搬水后需要重算一次给贴图。
                int percent = WaterHelper.GetCurrentCapacity(item);
                if (isTank)
                {
                    if (ReservoirRuntime.Applies(item))
                    {
                        ReservoirRuntime.Tick(item);
                        percent = WaterHelper.GetCurrentCapacity(item);
                    }
                    else
                    {
                        Refill(item, percent, dragging);
                        TryReserve(item, false);
                    }
                }
                SyncSprite(item, percent);
            }

            // 电话名片由 OnGameLoadedLate + OnNewGameStarted 两个 hook 各装一次即可：
            // PhoneClientDict 是 ES3 存档字段，写进去就持久化；巡检每帧查字典是浪费。
            PruneCache();
        }

        /// <summary>成堆判定；命中时只提示一次（不再做任何状态改写）。</summary>
        internal static bool IsStacked(GameItem item)
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
        /// R3：机器槽里的水箱在主池接近满时，把一部分水精确搬进内胆，给夜间机器留出余量。
        ///
        /// 只对湿气农场生效：它的 guard 是 `GetFreeCapacity(槽内物品) != 0`，只看主池；
        /// 主池满时整晚不跑，主池差一点时又只收那一点、其余随临时壶销毁。
        ///
        /// 0.14.0 默认只在 OnHandlingNightlyServicesLate 预腾一次：
        ///   · AutoReserve=开：按当夜预期产量 + 0.5 L 预留，常产 850 ml 时只腾约 1.35 L；
        ///   · Continuous=开：每 tick 维持目标，主池会显示略少（旧行为）；
        ///   · 生产结束后 OnGoingSleepLate 把剩余安全余量回填主池，白天恢复 100%。
        ///
        /// 失败一律吃掉：单台水箱异常不能中断 0.25 s 巡检或过夜结算。
        /// </summary>
        private static void TryReserve(GameItem tank, bool force)
        {
            try
            {
                if (tank == null || !tank.IsTag(Ids.TankTag)) return;
                if (ReserveConfig.ReservoirMode) return;
                if (IsStacked(tank)) return;
                if (IsDragging(tank)) return;

                int configLiters = ReserveConfig.ReserveLiters;
                if (configLiters <= 0) return;

                // 默认只在夜间预腾；Continuous 关闭时 tick 里直接返回，省掉容量/液体读取。
                if (!force && !ReserveConfig.Continuous) return;

                int cap = WaterHelper.GetTotalCapacity(tank);
                if (cap <= 1) return;

                int maxReserve = Math.Min(configLiters * 1_000_000, cap - 1);
                if (maxReserve <= 0) return;

                int free = WaterHelper.GetFreeCapacity(tank);
                if (free >= maxReserve) return;

                // 机器容器槽都是 GameSlotInventory；先做廉价过滤，避免给背包/网格里的水箱查父链。
                var inv = tank.parentInventory;
                if (!(inv is GameSlotInventory)) return;

                int id = tank.GetUniqueID();
                float now = Time.unscaledTime;
                int reserve;

                // 非强制巡检：正/负结果都缓存；正数缓存避免每 tick 重算当夜产量。
                if (!force && id != 0 && ReservePlans.TryGetValue(id, out ReservePlan plan)
                    && plan.Inv == inv && now < plan.Until)
                {
                    reserve = plan.Parts;
                    if (free >= reserve) return;
                }
                else
                {
                    GameItem machine = inv.GetParentItem();
                    if (machine == null || !Ids.MoistureFarmId.Equals(machine.identifier))
                    {
                        StorePlan(id, inv, 0, now + Ids.ReserveNegativeCacheSeconds);
                        return;
                    }

                    if (!CanMachineRun(machine))
                    {
                        StorePlan(id, inv, 0, now + Ids.ReserveNegativeCacheSeconds);
                        return;
                    }

                    reserve = ComputeReserve(machine, maxReserve);
                    StorePlan(id, inv, reserve, now + Ids.ReservePositiveCacheSeconds);
                    if (free >= reserve) return;
                }

                // 连续模式：reserve 超过容量 8% 会把主池压到 R2 的 90% 阈值以下，强制退回夜间模式。
                if (!force && (long)reserve * 100L > (long)cap * Ids.ReserveContinuousMaxPercent) return;

                int need = reserve - free;
                if (need <= 0) return;

                List<GameItem> cells = Cells(tank, out int stacked);
                if (cells.Count == 0)
                {
                    if (stacked == 0)
                        Log.Once("r3-no-cell", "R3：机器槽水箱没有内胆，无法为夜间产量腾出空间");
                    else
                        Log.Once("r3-stacked", "R3：内胆全部处于堆叠状态，无法腾出空间；请拆成单件");
                    return;
                }

                int moved = 0;
                for (int i = 0; i < cells.Count && moved < need; i++)
                {
                    GameItem cell = cells[i];
                    if (cell == null) continue;

                    int cellFree = WaterHelper.GetFreeCapacity(cell);
                    int take = Math.Min(need - moved, cellFree);
                    if (take <= 0) continue;

                    moved += LiquidMove.Move(tank, cell, take);
                }

                if (moved > 0)
                    Log.Info("R3：为机器槽水箱腾出 " + moved + " part（" + (moved / 1000) + " ml）");
                if (moved < need)
                    Log.Once("r3-short", "R3：内胆余量不足，仅腾出 " + moved + "/" + need + " part；机器可能丢水");
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("r3", "R3 预腾失败（本次跳过）：" + e.Message);
            }
        }

        private static void StorePlan(int id, GameInventory inv, int parts, float until)
        {
            if (id == 0) return;
            ReservePlans[id] = new ReservePlan { Inv = inv, Parts = parts, Until = until };
        }

        /// <summary>
        /// 按湿气农场当夜预期产量计算预留量；AutoReserve 关闭时直接用配置上限。
        /// 极端上限 21.471 L/夜（GetOutputVolume 的 int 上界），默认 25 L 上限足够覆盖。
        /// </summary>
        private static int ComputeReserve(GameItem machine, int maxParts)
        {
            if (!ReserveConfig.AutoReserve) return maxParts;

            try
            {
                int perFarm = MachineMoistureFarm.GetOutputVolume(machine);
                int sharing = MachineMoistureFarm.GetSharingFarmCount(machine);
                if (sharing < 1) sharing = 1;

                int expected = MachineMoistureFarm.ApplySharedAir(perFarm, sharing);
                long parts = (long)expected + Ids.ReserveMarginParts;
                if (parts < Ids.ReserveMarginParts) parts = Ids.ReserveMarginParts;
                if (parts > maxParts) parts = maxParts;
                return (int)parts;
            }
            catch (Exception e)
            {
                Log.Once("r3-output", "读取湿气农场产量失败（按配置上限预留）：" + e.Message);
                return maxParts;
            }
        }

        /// <summary>复刻 MachineMoistureFarm#CanRunTonight 的「电量/损坏/拥有」部分，去掉 free-capacity（那正是 R3 要修的）。</summary>
        private static bool CanMachineRun(GameItem machine)
        {
            if (machine == null) return false;
            if (machine.IsTag("BROKEN_MACHINE_TAG")) return false;
            if (!GeneralHelper.IsItemOwned(machine)) return false;
            if (machine.contentWindow == null) return false;

            GameItem battery = MachineHelper.GetBatterySlot(machine)?.childItem;
            if (battery == null) return false;

            return PowerHelper.CanDrawPowerSource(battery, MachineryHelper.GetMachinePowerUsage(machine));
        }

        /// <summary>
        /// 夜间预腾：在 HandlePower 之后、机器 onCycleEndSlotItemFunc 循环之前跑一次。
        /// 由 ModHook.OnHandlingNightlyServicesLate 触发（PlayerStore.cs:1820，早于 :1833 的机器循环）。
        /// </summary>
        internal static void OnNightlyServicesLate()
        {
            if (!Reentry.TryEnter()) return;
            try
            {
                // 一次/晚的全量刷新，兜住「当天最后一刻才把水箱塞进机器槽」。
                TankRegistry.RefreshFromStore();
                TankRegistry.Collect();

                List<GameItem> all = TankRegistry.Live;
                for (int i = 0; i < all.Count; i++)
                {
                    GameItem item = all[i];
                    if (item == null || !item.IsTag(Ids.TankTag)) continue;
                    if (ReserveConfig.ReservoirMode) ReservoirRuntime.PrepareForNight(item);
                    else TryReserve(item, true);
                }
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("r3-night", "R3 夜间预腾失败：" + e.Message);
            }
            finally
            {
                Reentry.Exit();
            }
        }

        /// <summary>
        /// 夜间回填：机器循环结束后，把预腾时留下的安全余量搬回主池，白天恢复 100%。
        /// 只在夜间模式（Continuous=false）生效；Continuous 由 tick 自己维持目标。
        /// 由 ModHook.OnGoingSleepLate 触发（PlayerStore.cs:1856，机器循环之后）。
        /// </summary>
        internal static void OnGoingSleepLate()
        {
            RestoreMachineTanks("r3-restore-night");
        }

        /// <summary>读档/新开局后把机器槽水箱回填到满（默认夜间模式）；Continuous 模式下不动。</summary>
        internal static void RestoreOnLoad()
        {
            RestoreMachineTanks("r3-restore-load");
        }

        private static void RestoreMachineTanks(string logKey)
        {
            if (ReserveConfig.ReservoirMode) return;
            if (ReserveConfig.Continuous || ReserveConfig.ReserveLiters <= 0) return;
            if (!Reentry.TryEnter()) return;
            try
            {
                TankRegistry.RefreshFromStore();
                TankRegistry.Collect();

                List<GameItem> all = TankRegistry.Live;
                for (int i = 0; i < all.Count; i++)
                {
                    GameItem item = all[i];
                    if (item == null || !item.IsTag(Ids.TankTag)) continue;
                    TryRestore(item);
                }
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once(logKey, "R3 夜间回填失败：" + e.Message);
            }
            finally
            {
                Reentry.Exit();
            }
        }

        /// <summary>只回填机器槽里的湿气农场水箱；其它容器/背包水箱不碰。</summary>
        private static void TryRestore(GameItem tank)
        {
            try
            {
                if (tank == null || !tank.IsTag(Ids.TankTag)) return;
                if (IsStacked(tank)) return;
                if (IsDragging(tank)) return;

                int free = WaterHelper.GetFreeCapacity(tank);
                if (free <= 0) return;

                var inv = tank.parentInventory;
                if (!(inv is GameSlotInventory)) return;

                GameItem machine = inv.GetParentItem();
                if (machine == null || !Ids.MoistureFarmId.Equals(machine.identifier)) return;

                List<GameItem> cells = Cells(tank, out int stacked);
                if (cells.Count == 0) return;

                int moved = 0;
                for (int i = 0; i < cells.Count && moved < free; i++)
                {
                    GameItem cell = cells[i];
                    if (cell == null) continue;

                    int cellWater = WaterHelper.GetTotalVolume(cell);
                    int take = Math.Min(free - moved, cellWater);
                    if (take <= 0) continue;

                    moved += LiquidMove.Move(cell, tank, take);
                }

                if (moved > 0)
                    Log.Info("R3：夜间回填 " + moved + " part（" + (moved / 1000) + " ml）");
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("r3-restore", "R3 夜间回填失败（本次跳过）：" + e.Message);
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
        internal static List<GameItem> Cells(GameItem tank, out int stacked)
        {
            var result = new List<GameItem>();
            stacked = 0;

            List<GameItem> list = null;
            try
            {
                var inv = MainCells(tank);
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
        /// 主格 inventory：优先按标识找 `mega_tank_cells`，兼容新窗口结构（主格 + 过滤器槽）。
        /// 旧结构没有标识时退回 `window.child`。
        /// </summary>
        private static GameInventory MainCells(GameItem tank)
        {
            var window = tank != null ? tank.contentWindow : null;
            if (window == null) return null;

            try
            {
                List<GraphNodeStorage> children = window.children;
                if (children != null)
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        if (children[i] is GameInventory inv
                            && string.Equals(inv.identifier, "mega_tank_cells", StringComparison.Ordinal))
                        {
                            return inv;
                        }
                    }
                }
            }
            catch
            {
                // 退回旧结构
            }

            try { return window.child as GameInventory; }
            catch { return null; }
        }

        /// <summary>
        /// 外部倒水时的溢出口：把 source 剩余的水精确搬进 target 水箱的内胆。
        /// 用于 `WaterHelper.TransferLiquid` 的 Postfix（倒水瓶/机器临时瓶不经过 R1 的 AddWater）。
        /// </summary>
        internal static int SpillSourceToCells(GameItem tank, GameItem source, int amount)
        {
            if (tank == null || source == null || amount <= 0) return 0;

            int stacked;
            List<GameItem> cells = Cells(tank, out stacked);
            int moved = 0;

            for (int i = 0; i < cells.Count && moved < amount; i++)
            {
                GameItem cell = cells[i];
                if (cell == null) continue;

                int cellFree = WaterHelper.GetFreeCapacity(cell);
                int take = Math.Min(amount - moved, cellFree);
                if (take <= 0) continue;

                moved += LiquidMove.Move(source, cell, take);
            }

            return moved;
        }

        /// <summary>
        /// 槽内统计：内胆个数、容量合计、水量合计。
        ///
        /// 用 long 累加：单件容量 2.0e9 part，16 件合计 3.2e10，int 会溢出成负数。
        /// 成堆的内胆按"占 1 格"计一次（一堆共享一份水，乘件数会算出假容量）。
        /// </summary>
        internal static void CellStats(GameItem tank, out int count, out long capacity, out long water)
        {
            count = 0;
            capacity = 0;
            water = 0;

            try
            {
                var inv = tank != null ? MainCells(tank) : null;
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
            if (ReservePlans.Count > 512) ReservePlans.Clear();
        }

        internal static bool IsDragging(GameItem item)
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

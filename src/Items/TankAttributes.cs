// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 原版属性对齐 + 老档迁移（0.12.0）。
    ///
    /// 为什么需要单独一层：
    ///   读档顺序是 <c>DirectoryMaster.Item(id)</c> 先跑工厂（新属性已经写好）→
    ///   再用存档里的 <c>state</c> / <c>itemTypes</c> 覆盖（`SaveManager#DecodeNodesInternal`）
    ///   → 最后 `PlayerStore` 逐物品调 <c>onLoaded</c>。
    ///   所以旧档里的基础价 0、`itemTypes` 里的 `NOT_FOR_RESALE`、state 里的
    ///   `CONTAINER_TAG` / `STORABLE_PACKAGE_TAG` 都会盖掉工厂新值，必须在
    ///   <c>onLoaded</c>（以及进档后的巡检兜底）里再校正一次。
    ///
    /// 纪律：
    ///   · 只认 `MEGA_TANK_TAG` / `MEGA_CELL_TAG`，绝不碰别人的物品；
    ///   · 只改 tag / itemTypes，不碰 shape / sprite（见 `TankSprites.ApplyBase` 的教训）；
    ///   · 全部幂等，重复调用不产生副作用。
    /// </summary>
    internal static class TankAttributes
    {
        private const string BasePriceTag = "LIQUID_CONTAINER_BASE_PRICE_TAG";

        /// <summary>迁移日志节流：同一只容器 30 秒内最多输出一次，避免巡检时刷屏。</summary>
        private static readonly Dictionary<int, float> LastMigrationLog = new Dictionary<int, float>();

        /// <summary>读档链末尾逐物品调用（此时 state / itemTypes 已从存档恢复）。</summary>
        internal static void OnLoaded(GameItem item)
        {
            Ensure(item);
        }

        /// <summary>进档 / 新开局 / 巡检兜底：扫店里所有本 mod 容器。返回处理个数。</summary>
        internal static int ApplyAll()
        {
            try
            {
                var emp = EmporiumEntry.Instance;
                if (emp == null) return 0;

                List<GameItem> all = emp.GetAllItems();
                if (all == null) return 0;

                int n = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    if (Ensure(all[i])) n++;
                }
                if (n > 0) Log.Info("原版属性对齐：" + n + " 个容器");
                return n;
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("attrs-all", "批量对齐属性失败：" + e.Message);
                return 0;
            }
        }

        /// <summary>把一件容器校正到 0.12.0 口径；返回是否真的改过。</summary>
        internal static bool Ensure(GameItem item)
        {
            if (item == null) return false;

            bool tank, cell;
            try
            {
                tank = item.IsTag(Ids.TankTag);
                cell = item.IsTag(Ids.CellTag);
            }
            catch
            {
                return false;
            }
            if (!tank && !cell) return false;

            bool changed = false;
            string why = "";
            try
            {
                // 1) 容器基础价：主池 350（原版 water_tank）/ 内胆 25（原版 water_pitcher）。
                int want = tank ? Ids.TankBasePrice : Ids.CellBasePrice;
                int have = item.GetTagReadonly(BasePriceTag).GetInt();
                if (have != want)
                {
                    item.ModifyTag(BasePriceTag, (TagState state) =>
                    {
                        state.Enable().SetInt(want);
                    }, skipValidate: true);
                    changed = true;
                    why += "价格 ";
                }

                // 2) NOT_FOR_RESALE 只留 tag，不留可见类型（0.12.0）。
                //    直接动 itemTypes 而不是 RemoveGameItemType：后者会 DisableTag + Validate，
                //    在 onLoaded 阶段没必要（tag 本来就在 state 里，只差一个可见条目）。
                List<string> types = item.itemTypes;
                if (types != null && types.Remove("NOT_FOR_RESALE"))
                {
                    changed = true;
                    why += "类型 ";
                }
                if (!item.IsTag("NOT_FOR_RESALE"))
                {
                    item.EnableTag("NOT_FOR_RESALE", skipValidate: true);
                    changed = true;
                    why += "不可转售tag ";
                }

                // 3) 主池：旧档的 CONTAINER_TAG + STORABLE_PACKAGE_TAG → CONTAINER_TOOLTIP_TAG。
                //    原版液体容器没有 CONTAINER_TAG；保留提示行用 CONTAINER_TOOLTIP_TAG。
                if (tank)
                {
                    if (item.DisableTag("CONTAINER_TAG", skipValidate: true)) { changed = true; why += "容器tag "; }
                    if (item.DisableTag("STORABLE_PACKAGE_TAG", skipValidate: true)) { changed = true; why += "可堆放tag "; }
                    if (item.EnableTag("CONTAINER_TOOLTIP_TAG", skipValidate: true)) { changed = true; why += "容器提示tag "; }
                    // 0.15.0：过滤槽。读档会用存档 state 覆盖工厂 state，旧档必须在这里补回。
                    if (item.EnableTag("FILTER_CONTAINER_TAG", skipValidate: true)) { changed = true; why += "过滤器tag "; }
                }

                if (changed)
                {
                    // 只改 tag：同步 modifiedState 让 IsTag / 估价立刻读到新值。
                    // 不走 ValidateShapeState：那是“改 shape/sprite”时用的重链路。
                    item.SyncModifiedState();

                    // Verbose 诊断：同一只容器 30 秒内最多一条，并写明到底改了什么。
                    // 若每 5 秒巡检都出现，可据此定位是哪个 tag/type 在反复变化。
                    int id = item.GetUniqueID();
                    float now = Time.unscaledTime;
                    if (!LastMigrationLog.TryGetValue(id, out float last) || now - last >= 30f)
                    {
                        LastMigrationLog[id] = now;
                        Log.Info("0.12.0 属性迁移：" + item.identifier + "（" + why.Trim() + "）");
                    }
                    if (LastMigrationLog.Count > 512) LastMigrationLog.Clear();
                }
                return changed;
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("attrs-" + item.identifier, "属性对齐失败：" + e.Message);
                return false;
            }
        }
    }
}

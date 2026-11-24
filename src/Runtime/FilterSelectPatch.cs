// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace MegaTank
{
    /// <summary>
    /// 多滤芯选择：原版 `WaterHelper.GetFilterItem` 只返回 `FindAllChildItems()` 里的
    /// **第一个** WATER_FILTER（HashSet 顺序不稳定）。水箱右侧有 4 个过滤器槽时，
    /// 需要按槽位顺序挑一个仍然有耐久的滤芯；0/1/2/3 = 左上/右上/左下/右下，
    /// 耗尽后自动用下一个。
    ///
    /// 只对 `MEGA_TANK_TAG` 生效，不改变水罐/其它容器的行为。
    /// Postfix 不跳过原方法；如果找不到可用滤芯，把结果置 null（等同原版耐久 0 不过滤）。
    /// </summary>
    internal static class FilterSelectPatch
    {
        internal static void Postfix(GameItem game, ref GameItem __result)
        {
            try
            {
                if (game == null || !game.IsTag(Ids.TankTag)) return;

                GameSlotInventory[] slots = FilterSlots(game);
                if (slots == null) return;

                for (int i = 0; i < slots.Length; i++)
                {
                    GameItem filter = slots[i] != null ? slots[i].childItem : null;
                    if (!IsUsableFilter(filter)) continue;

                    __result = filter;
                    return;
                }

                // null = 本水箱没有可用滤芯：不要退回原版 Hash 顺序里的耗尽滤芯。
                __result = null;
            }
            catch (Exception e)
            {
                Log.Once("filter-select", "多滤芯选择失败（本次按原版结果）：" + e.Message);
            }
        }

        private static bool IsUsableFilter(GameItem filter)
        {
            if (filter == null) return false;
            if (!filter.IsTag("WATER_FILTER")) return false;
            if (filter.identifier != null && filter.identifier.Equals("uv_filter")) return false;

            try { return DurabilityHelper.GetRealDurability(filter) > 0; }
            catch { return false; }
        }

        /// <summary>按 0..3 槽位从窗口子节点里找；槽位由 TankItems 的 GameSlotInventory 创建。</summary>
        private static GameSlotInventory[] FilterSlots(GameItem tank)
        {
            var window = tank != null ? tank.contentWindow : null;
            if (window == null) return null;

            List<GraphNodeStorage> children = window.children;
            if (children == null) return null;

            var slots = new GameSlotInventory[4];
            bool any = false;
            for (int i = 0; i < children.Count; i++)
            {
                var slot = children[i] as GameSlotInventory;
                if (slot == null || slot.identifier == null) continue;

                for (int n = 0; n < slots.Length; n++)
                {
                    if (string.Equals(slot.identifier, "mega_tank_filter_" + n, StringComparison.Ordinal))
                    {
                        slots[n] = slot;
                        any = true;
                        break;
                    }
                }
            }

            return any ? slots : null;
        }
    }
}

// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace MegaTank
{
    /// <summary>
    /// 本 mod 容器的轻量索引（0.11.6，P0 性能改造）。
    ///
    /// 为什么需要：
    ///   TankRuntime.Tick 每 0.25 s 调一次 EmporiumEntry.GetAllItems()，会把存档里
    ///   所有物品都遍历一遍；本 mod 只关心水箱/内胆，没必要每 tick 扫全店。
    ///
    /// 生命周期（与“读档会重建全部 GameItem”的硬规则对齐）：
    ///   · 工厂创建时 <see cref="Track"/>，只存弱引用，不持有对象；
    ///   · 进档 / 新开局 <see cref="RefreshFromStore"/> 一次（只取店里当前存在的容器）；
    ///   · Tick 里每 <see cref="Ids.RegistryRefreshSeconds"/> 秒刷新一次，兜住
    ///     “玩家从背包把水箱搬进店里”的情况；
    ///   · 读档整体替换引用；已销毁/重复的项在 <see cref="Collect"/> 里剔除。
    ///
    /// ★ 为什么不用 GameItem.onOneSecondItemFunc：
    ///   ChemicalHelper.RunNightlyChemicalSimulation 在过夜时会对同一批物品循环 360 次
    ///   调用它（FrameTicker 头注释也记录了这一点）。把回补/贴图挂上去会被放大 360 倍，
    ///   所以继续用自建 FrameTicker + 自己的索引。
    /// </summary>
    internal static class TankRegistry
    {
        private static readonly List<WeakReference<GameItem>> Refs = new List<WeakReference<GameItem>>();
        private static readonly List<GameItem> LiveItems = new List<GameItem>();
        private static readonly HashSet<int> SeenIds = new HashSet<int>();

        private static float _nextRefreshAt;
        private static bool _dirty = true;
        private static int _lastLoggedCount = -1;

        /// <summary>最近一次 <see cref="Collect"/> 得到的可用对象（只读使用，不要改）。</summary>
        internal static List<GameItem> Live => LiveItems;

        /// <summary>进档 / 新开局 / 重新启用时调用：下一次 Tick 强制从店里重建。</summary>
        internal static void MarkDirty()
        {
            _dirty = true;
            _nextRefreshAt = 0f;
        }

        /// <summary>停用 / 退出时调用，避免静态列表长期持有旧存档的弱引用。</summary>
        internal static void Clear()
        {
            Refs.Clear();
            LiveItems.Clear();
            SeenIds.Clear();
            _dirty = true;
            _nextRefreshAt = 0f;
            _lastLoggedCount = -1;
        }

        /// <summary>工厂创建物品时登记（此时 uniqueId 可能还是 0，Collect 时再取）。</summary>
        internal static void Track(GameItem item)
        {
            if (item == null) return;
            if (Refs.Count >= 256) Collect();
            Refs.Add(new WeakReference<GameItem>(item));
        }

        /// <summary>
        /// 从店里重新收集本 mod 容器。进档 / 新开局 / 周期兜底调用；
        /// 只收 EmporiumEntry 里的物品，保持与旧 Tick 的作用域一致。
        /// </summary>
        internal static void RefreshFromStore()
        {
            Refs.Clear();
            var emp = EmporiumEntry.Instance;
            if (emp == null) return;

            List<GameItem> all;
            try { all = emp.GetAllItems(); }
            catch (Exception e)
            {
                Log.Once("registry-scan", "容器索引扫描失败（下次重试）：" + e.Message);
                return;
            }
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                GameItem item = all[i];
                if (item == null) continue;
                bool ours;
                try { ours = item.IsTag(Ids.TankTag) || item.IsTag(Ids.CellTag); }
                catch { continue; }
                if (ours) Refs.Add(new WeakReference<GameItem>(item));
            }
            _dirty = false;
        }

        /// <summary>到点就刷新；返回本次是否刷新过（用于低频容量兜底）。</summary>
        internal static bool EnsureFresh(float now, float interval)
        {
            if (!_dirty && now < _nextRefreshAt) return false;
            if (EmporiumEntry.Instance == null) { _dirty = true; return false; }

            _dirty = false;
            _nextRefreshAt = now + interval;
            RefreshFromStore();
            return true;
        }

        /// <summary>把弱引用收敛成当前可用列表；已销毁 / 重复 uniqueId 的项顺手剔除。</summary>
        internal static void Collect()
        {
            LiveItems.Clear();
            SeenIds.Clear();

            for (int i = Refs.Count - 1; i >= 0; i--)
            {
                GameItem item;
                try
                {
                    if (!Refs[i].TryGetTarget(out item)) { Refs.RemoveAt(i); continue; }
                }
                catch { Refs.RemoveAt(i); continue; }

                if (item == null || IsDestroyed(item)) { Refs.RemoveAt(i); continue; }

                int id;
                try { id = item.GetUniqueID(); }
                catch { continue; }

                if (id != 0 && !SeenIds.Add(id)) { Refs.RemoveAt(i); continue; }
                LiveItems.Add(item);
            }

            if (LiveItems.Count != _lastLoggedCount)
            {
                _lastLoggedCount = LiveItems.Count;
                Log.Info("容器索引：" + LiveItems.Count + " 个（每 tick 只遍历这些）");
            }
        }

        private static bool IsDestroyed(GameItem item)
        {
            var element = item as GameItemElement;
            return element != null && element.IsDestroyed();
        }
    }
}

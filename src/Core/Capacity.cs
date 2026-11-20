using System;
using System.Collections.Generic;

namespace MegaTank
{
    /// <summary>
    /// 扩容等级（存档级全局）+ 把容量应用到容器。
    ///
    /// 存档语义（已核对 ModHelper 源码）：
    ///   `ModHelper.SetModDataInt` 底层是 `PlayerStore.Instance.ModData()`——
    ///   **随存档保存**，不是跨存档全局。所以：
    ///     · 同一个存档内，所有主池/内胆共享一个等级（买一次全部变大）；
    ///     · 不同存档各算各的；
    ///     · 新档从 0 档（100 L）开始。
    ///
    /// 三条纪律：
    ///   1. **只在进档后可读写**：ModDataStore 在没读档时返回 null（官方会打 Warning），
    ///      所以 GetLevel 在菜单阶段一律返回 0，写操作静默丢弃。
    ///   2. **不改物品 shape / sprite**：只动 LIQUID_CONTAINER_CAPACITY 这一个 tag。
    ///      LESSONS A7 的教训——任何触发 ValidateShapeState 的调用都会污染上层缓存。
    ///   3. **水位超限交给原版**：容量变大时水量不变；万一出现水量 > 容量，
    ///      `WaterHelper.GetCurrentCapacity` 系列会按原版语义处理，我们不做额外截断。
    /// </summary>
    internal static class Capacity
    {
        private const string LevelKey = "expand_level";

        /// <summary>容量口径版本。缺失/小于当前值 ⇒ 进档时按 <see cref="EnsureScheme"/> 迁移一次。</summary>
        private const string SchemeKey = "cap_scheme";

        private const int SchemeVersion = 2;

        private static int _cachedLevel;
        private static bool _cacheValid;

        /// <summary>当前扩容等级（0..10）。未进档返回 0。</summary>
        internal static int Level
        {
            get
            {
                if (_cacheValid) return _cachedLevel;
                try
                {
                    int v = ModHelper.GetModDataInt(Ids.ModId, LevelKey, 0);
                    if (v < 0) v = 0;
                    if (v > Ids.MaxExpandLevel) v = Ids.MaxExpandLevel;
                    _cachedLevel = v;
                    _cacheValid = true;
                    return v;
                }
                catch (Exception e)
                {
                    Log.Once("cap-read", "读扩容等级失败（按 0 档）：" + e.Message);
                    return 0;
                }
            }
        }

        /// <summary>当前容量（ml）。</summary>
        internal static int CurrentMl => Ids.CapacityAt(Level);

        /// <summary>还能不能再扩。</summary>
        internal static bool CanUpgrade => Level < Ids.MaxExpandLevel;

        /// <summary>进档后清缓存，让下次读取走存档真实值。</summary>
        internal static void Invalidate()
        {
            _cacheValid = false;
            _cachedLevel = 0;
        }

        /// <summary>
        /// 升一档并**立即**应用到场上所有容器（用户要求：购买后马上生效）。
        /// 返回新等级；已满级或写档失败返回 -1。
        /// </summary>
        internal static int Upgrade()
        {
            if (!CanUpgrade) return -1;

            int next = Level + 1;
            try
            {
                ModHelper.SetModDataInt(Ids.ModId, LevelKey, next);
            }
            catch (Exception e)
            {
                Log.Once("cap-write", "写扩容等级失败：" + e.Message);
                return -1;
            }

            _cachedLevel = next;
            _cacheValid = true;

            int applied = ApplyAll();
            Log.Info("扩容至 Lv." + next + "（" + Ids.CapacityAt(next) + " ml），已应用到 " + applied + " 个容器");
            return next;
        }

        /// <summary>扩容口径迁移（0.9.x → 0.10.0）：老档等级 +8 保住容量，只做一次。</summary>
        internal static void EnsureScheme()
        {
            try
            {
                if (ModHelper.GetModDataInt(Ids.ModId, SchemeKey, 0) >= SchemeVersion) return;

                int level = ModHelper.GetModDataInt(Ids.ModId, LevelKey, 0);
                if (level < 0) level = 0;
                if (level > Ids.MaxExpandLevel) level = Ids.MaxExpandLevel;

                int old = level;
                if (level > 0)
                {
                    level = Math.Min(level + Ids.LegacyLevelOffset, Ids.MaxExpandLevel);
                    if (level != old) ModHelper.SetModDataInt(Ids.ModId, LevelKey, level);
                }

                ModHelper.SetModDataInt(Ids.ModId, SchemeKey, SchemeVersion);
                Invalidate();

                if (level != old)
                {
                    Log.Info("扩容等级已迁移到新口径：Lv." + old + " → Lv." + level +
                             "（容量保持 " + Ids.CapacityAt(level) + " ml）");
                }
            }
            catch (Exception e)
            {
                // 迁移失败不阻塞进档：按当前等级照常算（可能比旧容量小，但功能正常）
                Log.Once("cap-scheme", "扩容口径迁移失败（按现有等级继续）：" + e.Message);
            }
        }

        /// <summary>
        /// 把当前容量写到场上所有本 mod 容器。返回处理个数。
        /// 只改容量 tag，不碰 shape/sprite（见类注释纪律 2）。
        /// </summary>
        internal static int ApplyAll()
        {
            try
            {
                var emp = EmporiumEntry.Instance;
                if (emp == null) return 0;

                List<GameItem> all = emp.GetAllItems();
                if (all == null) return 0;

                int ml = CurrentMl;
                int n = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    GameItem item = all[i];
                    if (item == null) continue;
                    if (!item.IsTag(Ids.TankTag) && !item.IsTag(Ids.CellTag)) continue;
                    if (ApplyTo(item, ml)) n++;
                }
                return n;
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("cap-apply-all", "批量应用容量失败：" + e.Message);
                return 0;
            }
        }

        /// <summary>把容量应用到一个容器；已有正确值则跳过（省一次 Validate）。</summary>
        internal static bool ApplyTo(GameItem item)
        {
            return ApplyTo(item, CurrentMl);
        }

        private static bool ApplyTo(GameItem item, int ml)
        {
            if (item == null || ml <= 0) return false;

            try
            {
                int wantParts = ml * 1000;
                int haveParts = item.ReadTagInt("LIQUID_CONTAINER_CAPACITY");
                if (haveParts == wantParts) return false;   // 已经对了，别白跑一次 Validate

                item.ModifyTag("LIQUID_CONTAINER_CAPACITY", (TagState state) =>
                {
                    state.Enable().SetInt(wantParts);
                });
                // 容量变化后让原版自己重算显示（tooltip 的水量行、水位贴图档位）
                item.Validate();
                return true;
            }
            catch (Exception e)
            {
                Log.Once("cap-apply-" + item.identifier, "应用容量失败：" + e.Message);
                return false;
            }
        }
    }
}

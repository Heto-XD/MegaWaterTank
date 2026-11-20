using System;

namespace MegaTank
{
    /// <summary>
    /// 王尔德（人脉）服务：两条可重复购买的项目。
    ///
    /// 关键事实（都已核对 1.0.0.16 源码）：
    ///   · **不能在主菜单注册**——`PlayerStore.Instance` 的 getter 会现场 new，构造函数在菜单阶段 NRE；
    ///     只在进档后（OnGameLoadedLate）调，且必须幂等（`GetDefinition` 判存在）。
    ///   · `cooldownDays > 0` ⇒ 官方自动设 `isRepeatable = true` + `cooldownDuration = N`，
    ///     买完当天冷却、次日恢复。这就是"可重复购买"，不需要 hack。
    ///   · 扣款顺序由 `WildNetworkUI.OnUseClicked` 固定：先恩惠（wildFavor），不足扣现金。
    ///   · 发放走 `ModHelper.SpawnItem` + `MoveItemToBackCounter`（与官方 `water_guide` 同路径）。
    /// </summary>
    internal static class Services
    {
        private const string Tab = "Water";
        private const string TankSvc = Ids.ModId + ".tank";
        private const string CellSvc = Ids.ModId + ".cell";
        private const string ExpandSvc = Ids.ModId + ".expand";

        internal const int TankCost = 500;
        internal const int CellCost = 250;

        /// <summary>
        /// 扩容服务单价（0.9.0）。定价依据：
        ///   · 原版 `WaterTankHelper` 的扩容先例：废金属 10 元 = +2 L ⇒ 约 5 元/L（材料价）。
        ///   · 本服务 150 元让**全部 17 个容器各 +100 L**（1,700 L 有效容量）⇒ 约 0.09 元/L，
        ///     比整机（500 元 / 3.8 吨 ≈ 0.13 元/L）略便宜，符合"批量升级"的定位。
        ///   · 满级总投入 1,500 元，相对水箱本体 500 + 16 内胆 4,000 的体系负担合理。
        ///   · `NetworkUpgrade.cost` 是固定 int（GetCost 只叠加难度/声望乘数），
        ///     因此**不支持递增定价**——想改价格只改这一个常量。
        /// </summary>
        internal const int ExpandCost = 150;

        /// <summary>扩容服务的冷却天数（可重复购买靠它；满级时会被 SyncExpandState 临时置 0）。</summary>
        internal const int ExpandCooldownDays = 1;

        private static bool _done;

        internal static void EnsureRegistered()
        {
            if (_done) return;

            try
            {
                bool missing = NetworkUpgrade.GetDefinition(TankSvc) == null
                               || NetworkUpgrade.GetDefinition(CellSvc) == null
                               || NetworkUpgrade.GetDefinition(ExpandSvc) == null;

                if (missing) Register();

                // 无论是否刚注册，进档都要同步一次：isRepeatable 是存档字段，
                // 别的存档/旧版本可能和当前扩容等级不一致（满级置灰靠它）。
                SyncExpandState();

                try
                {
                    ModHelper.SetNetworkTabLabel(Tab, () => Text.T("megatank.svc.tab", "储水", "Water"));
                }
                catch (Exception e)
                {
                    Log.Once("svc-tab", "页签命名失败：" + e.Message);
                }

                _done = true;
            }
            catch (Exception e)
            {
                // 不置 _done：下次进档重试（例如 PlayerStore 还没就绪）
                Log.Once("svc-init", "服务注册失败（下次进档重试）：" + e.Message);
            }
        }

        private static void Register()
        {
            Add(TankSvc, TankCost, "megatank.svc.tank", "mega_tank_icon", GrantTank);
            Add(CellSvc, CellCost, "megatank.svc.cell", "mega_cell_icon", GrantCell);
            Add(ExpandSvc, ExpandCost, "megatank.svc.expand", "mega_expand_icon", GrantExpand,
                cooldownDays: ExpandCooldownDays);
        }

        private static void Add(string id, int cost, string keyPrefix, string iconKey, Action onUnlock,
                                int cooldownDays = 1)
        {
            // 扩容项的"已购完"文案要带当前容量，其它项用通用文案
            bool isExpand = id == ExpandSvc;
            ModHelper.AddNetworkUpgrade(
                Ids.ModId,
                id,
                cost,
                subtitle: Text.T("megatank.svc.subtitle", "储水", "Water"),
                title: () => Text.T(keyPrefix + ".title", "服务", "Service"),
                description: () => Text.T(keyPrefix + ".desc", "", ""),
                onUnlock: () =>
                {
                    try { onUnlock(); }
                    catch (Exception e) { Log.Once("svc-unlock-" + id, id + " 发放失败：" + e.Message); }
                },
                tab: Tab,
                iconKey: Ids.ModId + ":" + iconKey,
                prerequisites: null,
                cooldownDays: cooldownDays,
                alreadyBought: isExpand
                    ? (Func<string>)(() => Text.T("megatank.svc.expand.maxed",
                        "已扩容至上限 {0} L", "Fully expanded to {0} L", Ids.CapacityAt(Ids.MaxExpandLevel) / 1000))
                    : (() => Text.T("megatank.svc.repeat", "可重复购买", "Repeatable")));
        }

        private static void GrantTank()
        {
            Grant(Ids.Tank);
            Grant(Ids.Cell);   // 首购：主池 + 1 个内胆（0.9.0 起起步各 0.9 吨，满级共 3.8 吨）
        }

        private static void GrantCell()
        {
            Grant(Ids.Cell);
        }

        /// <summary>
        /// 扩容服务：等级 +1 并立即应用到场上所有容器（用户要求"购买后马上生效"）。
        /// 满级时不该走到这里——按钮已被 SyncExpandState 置灰；
        /// 万一被点（竞态/其它 mod 改状态），这里直接拒绝且**不扣费**做不到（扣费在官方 UI），
        /// 所以补一次退款并提示。
        /// </summary>
        private static void GrantExpand()
        {
            int lv = Capacity.Upgrade();
            if (lv >= 0)
            {
                SyncExpandState();
                return;
            }

            // 满级却仍被购买：退回等额现金，避免玩家白花钱
            try
            {
                PlayerStore.Instance.playerCash += ExpandCost;
                Log.Once("expand-maxed", "已在满级仍收到扩容购买，已退款 " + ExpandCost + "；请把这条日志报给作者");
            }
            catch (Exception e)
            {
                Log.Once("expand-refund", "满级退款失败：" + e.Message);
            }
        }

        /// <summary>
        /// 满级时把扩容项切成"已完成"态，官方 UI 就会自动**置灰并隐藏购买按钮**
        /// （需求里的"满级置灰"），不需要任何新补丁。
        ///
        /// 判据链（源码 WildNetworkUI.cs，两处读取点都要照顾）：
        ///   · 卡片 `RefreshSlot`：`flag = IsUnlocked()`；
        ///       flag=true ⇒ 价格标签整块 `SetActive(false)`、图标 alpha 降到 0.7（视觉变灰）。
        ///   · 详情 `ShowInfo`：`flag2 = IsUnlocked()`；
        ///       命中 `isRepeatable &amp; flag2` 分支 ⇒ `useButton.SetActive(false)`，
        ///       并显示 `alreadyBoughtProvider` 文案「已扩容至上限 1900 L」。
        ///   而 `IsUnlocked()` 只认 `state == 2`。
        ///
        /// ★ 关键坑（第一版踩过）：光设 `state = 2` 不够——可重复项每次购买后
        ///   `Unlock()` 会把 `state` 从 2 打回 1（NetworkUpgrade.cs:889-895，
        ///   条件是 `cooldownDuration != 0`）。所以满级时必须**同时把
        ///   `cooldownDuration` 置 0**，state 才停得住。
        ///   未满级时恢复 `cooldownDuration = 1` + `state = 1`（正常可购买状态）。
        ///
        /// 自愈：三个字段都是 [ES3Serializable]，可能跟着存档走；每次进档
        /// `EnsureRegistered` 都会调本方法重设一次，所以存档里的脏值会被纠正。
        /// </summary>
        private static void SyncExpandState()
        {
            try
            {
                NetworkUpgrade def = NetworkUpgrade.GetDefinition(ExpandSvc);
                if (def == null) return;

                bool maxed = !Capacity.CanUpgrade;
                int wantState = maxed ? 2 : 1;
                int wantCooldown = maxed ? 0 : ExpandCooldownDays;

                bool changed = false;
                if (def.isRepeatable != maxed) { def.isRepeatable = maxed; changed = true; }
                if (def.state != wantState) { def.state = wantState; changed = true; }
                if (def.cooldownDuration != wantCooldown) { def.cooldownDuration = wantCooldown; changed = true; }
                if (maxed && def.cooldownCurrent != 0) { def.cooldownCurrent = 0; changed = true; }

                if (changed)
                {
                    Log.Info("扩容服务状态已同步：满级=" + maxed
                        + "（state=" + wantState + " cooldownDuration=" + wantCooldown + "）");
                    try { WildNetworkUI.RefreshAll(); } catch { }
                }
            }
            catch (Exception e)
            {
                Log.Once("expand-sync", "同步扩容服务状态失败：" + e.Message);
            }
        }

        private static void Grant(string itemId)
        {
            var item = ModHelper.SpawnItem(itemId);
            if (item == null)
            {
                Log.Once("grant-" + itemId, "生成 " + itemId + " 失败（服务已扣费，请在日志上报此条）");
                return;
            }

            ModHelper.MoveItemToBackCounter(item);
        }
    }
}

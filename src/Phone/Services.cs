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
    ///     **但扩容服务故意用 cooldownDays=0**（无冷却，当天可连买）——那种情况下
    ///     Unlock() 不会把 state 打回 1，必须自己复位，见 ExpandCooldownDays 的注释。
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

        /// <summary>
        /// 扩容服务的冷却天数：**0 = 不设冷却，可以当天连买**（用户 2026-11-20 拍板）。
        ///
        /// 代价与对策（源码推导，动它之前先看这段）：
        ///   `AddNetworkUpgrade` 里 `cooldownDays=0` ⇒ `isRepeatable=false` + `cooldownDuration=0`
        ///   （ModHelper.cs:338-339）。而 `Unlock()`（NetworkUpgrade.cs:882-896）只有在
        ///   `cooldownDuration != 0` 时才会把 `state` 从 2 打回 1 ⇒ **不设冷却的话，
        ///   买一次后 state 停在 2，IsUnlocked() 恒 true，按钮从此置灰再也买不了**。
        ///   对策：`GrantExpand` 在购买成功后手动把 state 复位成 1（未满级时），
        ///   满级时交给 `SyncExpandState` 保持 2（= 置灰）。onUnlock 是在 Unlock() 内部
        ///   `state = 2` 之后才回调的（L884→L885），所以覆盖得住。
        /// </summary>
        internal const int ExpandCooldownDays = 0;

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

        /// <summary>
        /// 停用时撤销注册：把三条服务从定义表与 `UnlockActions` 里摘掉。
        ///
        /// 为什么必须撤（根 AGENTS 硬规则 25）：`AddNetworkUpgrade` 是 `Add*` 系注册，
        /// 不配 `Remove*` 就是"关了 mod 设置还在"——同进程内停用后服务页仍能看到三条，
        /// 点下去还会调到本 mod 的 `onUnlock`（等于 mod 还在跑）。
        ///
        /// 语义（game-kb `UPG-023` / `UPG-025`）：`RemoveNetworkUpgrades` 只摘静态定义与
        /// 解锁动作，**不动** `PlayerStore.networkUpgrade` 里的存档记录 ⇒
        /// 重装/重开 mod 时购买记录与等级都还在，不丢数据。
        /// `_done` 必须一起复位，否则重新启用时 `EnsureRegistered` 直接早退、服务再也回不来。
        /// </summary>
        internal static void Unregister()
        {
            try
            {
                int n = ModHelper.RemoveNetworkUpgrades(Ids.ModId);
                if (n > 0) Log.Info("服务注册已撤销（" + n + " 条）");
            }
            catch (Exception e)
            {
                Log.Once("svc-unreg", "撤销服务注册失败：" + e.Message);
            }
            finally
            {
                _done = false;
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
                // 扩容项的文案随等级变：未满级报"当前→下一档"，满级改成"已达上限"。
                // 否则满级时描述仍写"可重复购买"，与下方"已扩容至上限"自相矛盾（实机截图反馈）。
                description: isExpand
                    ? (Func<string>)(() =>
                    {
                        int curL = Capacity.CurrentMl / 1000;
                        int maxL = Ids.CapacityMl / 1000;
                        return Capacity.CanUpgrade
                            ? Text.T("megatank.svc.expand.desc",
                                "把水箱和内胆的容量永久提升 100 L。当前 {0} L → 下一档 {1} L（上限 {2} L）。全部容器同时生效，可重复购买。",
                                "Permanently raises the capacity of every tank and cell by 100 L. Now {0} L -> next {1} L (max {2} L). Applies to all containers at once. Repeatable.",
                                curL, curL + Ids.ExpandStepMl / 1000, maxL)
                            : Text.T("megatank.svc.expand.desc.maxed",
                                "已到上限 {0} L，全部容器都已满容量，无法继续扩建。",
                                "Already at the {0} L ceiling; every container is maxed out.",
                                maxL);
                    })
                    : (() => Text.T(keyPrefix + ".desc", "", "")),
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
        /// <summary>
        /// 取扩容项的**运行时副本**——这是 UI 真正读的那个对象。
        ///
        /// ★ 0.9.1 的坑：`NetworkUpgrade.GetDefinition(id)` 返回的是**静态模板**
        ///   （`definitions` 字典），而 `WildNetworkUI` 全程读
        ///   `PlayerStore.Instance.networkUpgrade[id]`（`ShowInfo` 的 L492、
        ///   `RefreshSlot` 的 L396）。运行时副本由 `CopyForRuntime()` 在**首次注册**时
        ///   一次性拷出、之后随存档走；`RegisterModUpgrade` 只在 key 不存在时才 Add
        ///   （NetworkUpgrade.cs），**已存在的运行时副本永远不跟着模板更新**。
        ///   所以老存档里 `cooldownDuration=1` 会一直留着，改模板完全无效
        ///   （实机表现：日志无"状态已同步"、面板仍写"冷却（1天）"）。
        ///
        /// 结论：凡是改 state / cooldownDuration / cooldownCurrent，**必须改运行时副本**；
        /// 模板只在"新存档第一次注册"时有意义。
        /// </summary>
        private static NetworkUpgrade Runtime()
        {
            try
            {
                var store = PlayerStore.Instance;
                if (store?.networkUpgrade != null
                    && store.networkUpgrade.TryGetValue(ExpandSvc, out var rt)
                    && rt != null)
                {
                    return rt;
                }
            }
            catch (Exception e)
            {
                Log.Once("expand-rt", "取扩容运行时副本失败：" + e.Message);
            }
            return NetworkUpgrade.GetDefinition(ExpandSvc);   // 兜底：刚注册还没进存档时
        }

        private static void GrantExpand()
        {
            int lv = Capacity.Upgrade();
            if (lv >= 0)
            {
                // 无冷却（ExpandCooldownDays=0）时官方 Unlock() 不会把 state 打回 1，
                // 买完就停在 2 ⇒ IsUnlocked() 恒 true ⇒ 按钮置灰、再也买不了。
                // 这里手动复位：未满级回到可购买态(1)，满级留给 SyncExpandState 设成 2。
                if (Capacity.CanUpgrade)
                {
                    try
                    {
                        NetworkUpgrade def = Runtime();
                        if (def != null)
                        {
                            def.state = 1;
                            def.cooldownCurrent = 0;
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Once("expand-reset", "扩容后复位可购买态失败（可能需重进电话）：" + e.Message);
                    }
                }

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
                // ★ 必须改运行时副本，不能改 GetDefinition 返回的静态模板（见 Runtime() 注释）
                NetworkUpgrade rt = Runtime();
                if (rt == null) return;

                bool maxed = !Capacity.CanUpgrade;
                int wantState = maxed ? 2 : 1;
                int wantCooldownDur = maxed ? 0 : ExpandCooldownDays;   // 现在两条分支都是 0
                bool wantRepeatable = maxed;

                bool changed = false;
                if (rt.isRepeatable != wantRepeatable) { rt.isRepeatable = wantRepeatable; changed = true; }
                if (rt.state != wantState) { rt.state = wantState; changed = true; }
                if (rt.cooldownDuration != wantCooldownDur) { rt.cooldownDuration = wantCooldownDur; changed = true; }
                // 无冷却 ⇒ 残留的冷却计数必须清掉，否则面板仍会显示"冷却（N天）"
                if (rt.cooldownCurrent != 0) { rt.cooldownCurrent = 0; changed = true; }

                // 静态模板也同步一份：新存档首次注册会从它 CopyForRuntime()
                try
                {
                    NetworkUpgrade tpl = NetworkUpgrade.GetDefinition(ExpandSvc);
                    if (tpl != null && !ReferenceEquals(tpl, rt))
                    {
                        tpl.isRepeatable = wantRepeatable;
                        tpl.cooldownDuration = wantCooldownDur;
                    }
                }
                catch { }

                if (changed)
                {
                    Log.Info("扩容服务状态已同步（运行时副本）：满级=" + maxed
                        + " state=" + rt.state + " cooldownDuration=" + rt.cooldownDuration
                        + " cooldownCurrent=" + rt.cooldownCurrent);
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

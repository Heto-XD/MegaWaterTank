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

        internal const int TankCost = 500;
        internal const int CellCost = 250;

        private static bool _done;

        internal static void EnsureRegistered()
        {
            if (_done) return;

            try
            {
                bool missing = NetworkUpgrade.GetDefinition(TankSvc) == null
                               || NetworkUpgrade.GetDefinition(CellSvc) == null;

                if (missing) Register();

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
        }

        private static void Add(string id, int cost, string keyPrefix, string iconKey, Action onUnlock)
        {
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
                cooldownDays: 1,
                alreadyBought: () => Text.T("megatank.svc.repeat", "可重复购买", "Repeatable"));
        }

        private static void GrantTank()
        {
            Grant(Ids.Tank);
            Grant(Ids.Cell);   // 首购：主池 + 1 个内胆（3.8 吨）
        }

        private static void GrantCell()
        {
            Grant(Ids.Cell);
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

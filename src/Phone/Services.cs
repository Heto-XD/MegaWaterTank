// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>电话商城的三件商品。</summary>
    internal enum ShopItem
    {
        Tank,
        Cell,
        Expand,
    }

    /// <summary>
    /// 电话商城（0.16.0 起完全自持）。
    ///
    /// ★ 为什么搬出「王尔德人脉」窗口（0.15.x 的做法）：
    ///   0.15.x 用 `ModHelper.AddNetworkUpgrade(..., tab:"Water")` 把三件商品塞进原版
    ///   `WildNetworkUI`。那个窗口同时承载原版「基础」和「高科技（tab="Advanced"）」两页，
    ///   玩家反馈变成"mod 抢了原版高科技升级界面"。三条结构性冲突：
    ///     · 页签是**全局静态命名空间**（`NetworkUpgrade.tab` + `tabLabels`），
    ///       `SetNetworkTabLabel` 没有官方撤销口，停用 mod 后标签仍占着；
    ///     · 窗口是单例 id `wilds_network`，`WildNetworkUI.Open()` 里 `if (IsOpen) return RefreshAll()`
    ///       会早退 —— 电话 UI 还压着时窗口就已 Show()，之后原版入口再打开只会刷新一个看不见的窗；
    ///     · `tab` 字段是 `[ES3NonSerializable]`、`subtitle` 却进存档，读档后口径不一致。
    ///   现在改成**只在自家电话对话里下单**：不注册升级、不加页签、不开原版窗口，
    ///   对原版界面零占用（根 `AGENTS.md` 硬规则 25 的"Add* 必配 Remove*"也一并满足）。
    ///
    /// ★ 存档兼容：老档里 `PlayerStore.networkUpgrade` 可能还留着 `megatank.tank/cell/expand` 三条
    ///   记录（`RemoveNetworkUpgrades` 只摘静态定义，不动存档）。这些记录没有定义就永远不会被画出来，
    ///   属于惰性数据，**不主动删存档**（删了才是动玩家存档）。<see cref="PurgeLegacy"/> 只清静态表。
    ///
    /// ★ 扣费口径与原版 `WildNetworkUI.OnUseClicked` 一致：先扣人情（wildFavor），不足再扣现金，
    ///   并记账给 `RunStats.OnWildUpgradeBought` —— 玩家的人情余额不会被 mod 躲过去。
    ///   价格乘数（难度/声望）不再经过 `NetworkUpgrade.GetCost`，这里就是最终价。
    /// </summary>
    internal static class Services
    {
        // ── 定价（唯一出处，改价只动这里）──
        // 依据：原版 `WaterTankHelper` 扩容先例 ≈ 5 元/L（材料价）；本 mod 单档 +100 L 覆盖全部容器。
        internal const int TankCost = 500;
        internal const int CellCost = 250;
        internal const int ExpandCost = 150;

        /// <summary>0.15.x 用过的页签名，0.16.0 起退役；仅用于清掉遗留标签。</summary>
        private const string LegacyTab = "Water";

        // ── 冷却（ModData，随存档走）──
        // 语义与 `NetworkUpgrade.OnDayStart` 对齐：买完置 1，起床（新的一天）减到 0。
        private const string CdTankKey = "svc_cd_tank";
        private const string CdCellKey = "svc_cd_cell";
        private const int CooldownDays = 1;

        /// <summary>
        /// 进档 / 新开局就绪：清 0.15.x 的遗留注册，并把存档里的冷却脏值夹回来。
        /// 幂等 —— 每次进档都跑，代价只是两次字典查询。
        /// </summary>
        internal static void BootReady()
        {
            PurgeLegacy();
            ClampCd(CdTankKey);
            ClampCd(CdCellKey);
        }

        /// <summary>停用 / 退出时撤销：把 0.15.x 的静态注册和页签标签摘干净。</summary>
        internal static void Unregister()
        {
            PurgeLegacy();
        }

        private static void PurgeLegacy()
        {
            try
            {
                int n = ModHelper.RemoveNetworkUpgrades(Ids.ModId);
                if (n > 0) Log.Info("已摘除 0.15.x 的人脉升级注册（" + n + " 条），商品改走电话对话");
            }
            catch (Exception e)
            {
                Log.Once("svc-purge-upg", "摘除旧人脉升级注册失败：" + e.Message);
            }

            try
            {
                // `tabLabels` 是静态表，没有公开 Remove；传 null 即摘除（NetworkUpgrade.RegisterTabLabel）。
                // 不清的话，同一进程内重开 mod 会把旧 lambda 一直挂在静态表上。
                NetworkUpgrade.RegisterTabLabel(LegacyTab, null);
            }
            catch (Exception e)
            {
                Log.Once("svc-purge-tab", "清除旧页签标签失败：" + e.Message);
            }
        }

        // ───────────────────────── 冷却 ─────────────────────────

        /// <summary>起床（新的一天）时递减冷却。由 <c>ModHook.OnWakingUpLate</c> 触发。</summary>
        internal static void OnWakingUp()
        {
            DecCd(CdTankKey);
            DecCd(CdCellKey);
        }

        private static int GetCd(string key)
        {
            try
            {
                int v = ModHelper.GetModDataInt(Ids.ModId, key, 0);
                return v < 0 ? 0 : v;
            }
            catch (Exception e)
            {
                Log.Once("svc-cd-read-" + key, "读冷却失败（按可购买处理）：" + e.Message);
                return 0;
            }
        }

        private static void SetCd(string key, int days)
        {
            try
            {
                ModHelper.SetModDataInt(Ids.ModId, key, days);
            }
            catch (Exception e)
            {
                Log.Once("svc-cd-write-" + key, "写冷却失败（下次进档会自愈）：" + e.Message);
            }
        }

        private static void DecCd(string key)
        {
            int v = GetCd(key);
            if (v > 0) SetCd(key, v - 1);
        }

        private static void ClampCd(string key)
        {
            int v = GetCd(key);
            if (v > CooldownDays) SetCd(key, CooldownDays);
        }

        // ───────────────────────── 状态查询 ─────────────────────────

        /// <summary>钱包口径 = 现金 + 人情（与 `WildNetworkUI` 的可用额度一致）。</summary>
        private static int Funds()
        {
            try
            {
                var ps = PlayerStore.Instance;
                if (ps == null) return 0;
                return ps.playerCash + ps.wildFavor;
            }
            catch (Exception e)
            {
                Log.Once("svc-funds", "读余额失败（按 0 处理）：" + e.Message);
                return 0;
            }
        }

        private static int CostOf(ShopItem item)
        {
            switch (item)
            {
                case ShopItem.Tank: return TankCost;
                case ShopItem.Cell: return CellCost;
                default: return ExpandCost;
            }
        }

        /// <summary>能不能买（供对话选项的 `choiceEnabled` 用，每次显示都会重新求值）。</summary>
        internal static bool CanBuy(ShopItem item)
        {
            if (item == ShopItem.Tank && GetCd(CdTankKey) > 0) return false;
            if (item == ShopItem.Cell && GetCd(CdCellKey) > 0) return false;
            if (item == ShopItem.Expand && !Capacity.CanUpgrade) return false;
            return Funds() >= CostOf(item);
        }

        // ───────────────────────── 菜单文案 ─────────────────────────

        /// <summary>
        /// 三个商品选项的按钮文字。
        /// ★ 电话对话里**按钮文字 = 选项指向的那句对话的正文**（`DialoguePhoneUIManager.DisplayChoice`
        ///   读的是 `choices[i].Item2.plainText`），所以文案必须在 PhoneCard 建图时写进那些节点，
        ///   而且买完要刷新一次，否则玩家会看到上一轮的状态。
        /// </summary>
        internal static string Label(ShopItem item)
        {
            switch (item)
            {
                case ShopItem.Tank:
                    return Text.T("megatank.shop.tank",
                        "巨型水箱 · {0}：主池 + 1 个内胆",
                        "Mega Water Tank - {0}: tank + 1 cell", TankCost) + Suffix(item);

                case ShopItem.Cell:
                    return Text.T("megatank.shop.cell",
                        "追加内胆 · {0}：1 个内胆",
                        "Extra cell - {0}: 1 cell", CellCost) + Suffix(item);

                default:
                    if (!Capacity.CanUpgrade)
                    {
                        return Text.T("megatank.shop.expand.maxed",
                            "扩容 · 已到上限 {0} L",
                            "Expand - already at {0} L", Ids.CapacityMl / 1000);
                    }
                    return Text.T("megatank.shop.expand",
                        "扩容 · {0}：全部容器 {1} → {2} L",
                        "Expand - {0}: every container {1} -> {2} L",
                        ExpandCost, Capacity.CurrentMl / 1000, (Capacity.CurrentMl + Ids.ExpandStepMl) / 1000)
                        + Suffix(item);
            }
        }

        /// <summary>后缀：当天已购 / 钱不够。</summary>
        private static string Suffix(ShopItem item)
        {
            bool cooling = (item == ShopItem.Tank && GetCd(CdTankKey) > 0)
                           || (item == ShopItem.Cell && GetCd(CdCellKey) > 0);
            if (cooling)
            {
                return Text.T("megatank.shop.suffix.cooldown", "（今天已经买过）", " (already bought today)");
            }
            if (Funds() < CostOf(item))
            {
                return Text.T("megatank.shop.suffix.nomoney", "（余额不足）", " (not enough funds)");
            }
            return "";
        }

        // ───────────────────────── 购买 ─────────────────────────

        /// <summary>
        /// 下单。**先校验再扣费**，任何一步失败都不动钱。
        /// 返回给玩家看的结果文案（成交 / 拒绝原因），由 PhoneCard 显示在下一句对话里。
        /// </summary>
        internal static string Buy(ShopItem item)
        {
            try
            {
                // 1) 状态闸（选项已置灰时点不动，这里是防竞态 / 防旧文案的第二道）
                if (item == ShopItem.Tank && GetCd(CdTankKey) > 0) return CooldownText();
                if (item == ShopItem.Cell && GetCd(CdCellKey) > 0) return CooldownText();
                if (item == ShopItem.Expand && !Capacity.CanUpgrade)
                {
                    return Text.T("megatank.shop.refuse.maxed",
                        "已经扩到上限 {0} L 了，没得再扩。",
                        "Already at the {0} L ceiling.", Ids.CapacityMl / 1000);
                }

                int cost = CostOf(item);
                if (Funds() < cost)
                {
                    return Text.T("megatank.shop.refuse.money",
                        "这单要 {0}，现金加人情不够。",
                        "That costs {0}; cash plus favours does not cover it.", cost);
                }

                // 2) 扣费（人情优先，与原版一致）
                if (!TryPay(cost)) return CooldownText();

                // 3) 发货
                switch (item)
                {
                    case ShopItem.Tank:
                        SetCd(CdTankKey, CooldownDays);
                        Grant(Ids.Tank);
                        Grant(Ids.Cell);      // 首购：主池 + 1 个内胆
                        PlayRegister();
                        Log.Info("电话下单：水箱（-" + cost + "），余额 " + Funds());
                        return Text.T("megatank.shop.bought.tank",
                            "成交。水箱和 1 个内胆已经放到后仓，余额 {0}。",
                            "Deal. The tank and 1 cell are in the back room. Balance {0}.", Funds());

                    case ShopItem.Cell:
                        SetCd(CdCellKey, CooldownDays);
                        Grant(Ids.Cell);
                        PlayRegister();
                        Log.Info("电话下单：内胆（-" + cost + "），余额 " + Funds());
                        return Text.T("megatank.shop.bought.cell",
                            "成交。1 个内胆已经放到后仓，余额 {0}。",
                            "Deal. 1 cell is in the back room. Balance {0}.", Funds());

                    default:
                        int lv = Capacity.Upgrade();
                        if (lv < 0)
                        {
                            // 写档失败：退款，避免玩家白花钱
                            Refund(cost);
                            Log.Once("svc-expand-fail", "扩容写档失败，已退款 " + cost);
                            return Text.T("megatank.shop.refuse.fail",
                                "扩容没写进存档，钱已经退给你了。",
                                "The expansion did not reach the save; your money was refunded.");
                        }
                        PlayRegister();
                        Log.Info("电话下单：扩容 Lv." + lv + "（-" + cost + "），余额 " + Funds());
                        return Text.T("megatank.shop.bought.expand",
                            "成交。全部容器现在是 {0} L，余额 {1}。",
                            "Deal. Every container is now {0} L. Balance {1}.",
                            Ids.CapacityAt(lv) / 1000, Funds());
                }
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("svc-buy", "下单异常（未扣费或已退款）：" + e.Message);
                return Text.T("megatank.shop.refuse.error",
                    "这单出岔子了，钱没动。把这条日志报给作者。",
                    "Something broke; you were not charged. Please report this log line.");
            }
        }

        private static string CooldownText()
        {
            return Text.T("megatank.shop.refuse.cooldown",
                "这个今天已经买过了，明天再来。",
                "You already bought that today. Come back tomorrow.");
        }

        /// <summary>扣费：先人情后现金。返回 false = 余额不足（不动钱）。</summary>
        private static bool TryPay(int cost)
        {
            try
            {
                var ps = PlayerStore.Instance;
                if (ps == null) return false;
                if (ps.playerCash + ps.wildFavor < cost) return false;

                int cashSpent = 0;
                if (ps.wildFavor >= cost)
                {
                    ps.wildFavor -= cost;
                }
                else
                {
                    cashSpent = cost - ps.wildFavor;
                    ps.wildFavor = 0;
                    ps.playerCash -= cashSpent;
                }

                try { RunStats.OnWildUpgradeBought(cashSpent); } catch (Exception) { }
                return true;
            }
            catch (Exception e)
            {
                Log.Once("svc-pay", "扣费失败（视为未扣）：" + e.Message);
                return false;
            }
        }

        private static void Refund(int amount)
        {
            try
            {
                var ps = PlayerStore.Instance;
                if (ps != null) ps.playerCash += amount;
            }
            catch (Exception e)
            {
                Log.Once("svc-refund", "退款失败：" + e.Message);
            }
        }

        private static void PlayRegister()
        {
            try
            {
                if (EmporiumAudioPlayer.Instance != null) EmporiumAudioPlayer.Instance.PlayRegister();
            }
            catch (Exception e)
            {
                Log.Once("svc-sound", "播放收银音效失败：" + e.Message);
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

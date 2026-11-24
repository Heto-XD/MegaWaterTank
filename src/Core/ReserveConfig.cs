// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// 水量自动化配置：R3 机器预腾 + 水库模式。必须在 IMod.Init（加载期）定义。
    ///
    /// 默认仍是机器预腾：
    ///   · ReservoirMode = false；
    ///   · 机器预腾 25 L 上限 / 自动 / 仅夜间；
    ///   · 水库目标 90%（玩家开启水库模式后才生效）。
    /// </summary>
    internal static class ReserveConfig
    {
        private static readonly string Section =
            CfgLocale.Sec("megatank.cfg.sec.water", "Water automation", "水量自动化");

        private static ConfigEntry<bool> _reservoirMode;
        private static ConfigEntry<int> _reservoirTarget;
        private static ConfigEntry<int> _reserveLiters;
        private static ConfigEntry<bool> _auto;
        private static ConfigEntry<bool> _continuous;
        private static bool _initialized;

        internal static void Init()
        {
            if (_initialized) return;

            try
            {
                var cfg = ModHelper.GetConfig(Ids.ModId);
                if (cfg == null) return;

                _reservoirMode = cfg.Define(
                    "ReservoirMode",
                    false,
                    Section,
                    () => Text.T("megatank.cfg.reservoir", "水库模式", "Reservoir mode"),
                    tooltip: () => Text.T("megatank.cfg.reservoir.tip",
                        "开：主池维持在目标水位附近，高于/低于死区时自动存入/回补子胆；R3 机器预腾设置将不生效。关：保持现有机器预腾行为。",
                        "On: keeps the main tank near the target level, moving water to/from cells outside the dead band; machine-reserve settings are ignored. Off: keeps the current machine-reserve behavior."));

                _reservoirTarget = cfg.Define(
                    "ReservoirTarget",
                    Ids.DefaultReservoirTargetPercent,
                    Section,
                    () => Text.T("megatank.cfg.reservoir.target", "水库目标水位（%）", "Reservoir target (%)"),
                    tooltip: () => Text.T("megatank.cfg.reservoir.target.tip",
                        "0–100%。默认 90 保持原有习惯；推荐 40–80 给机器留余量，90 以上时极端产量可能没有足够空间。目标 ±5% 内不搬水。",
                        "0–100%. Default 90 keeps the previous habit; 40–80 is recommended to leave room for machines. Above 90 an extreme output may not fit. No movement within ±5% of the target."));

                _reservoirTarget.OnChanged += OnReservoirTargetChanged;
                int initialTarget = _reservoirTarget.Value;
                int clampedTarget = ClampPercent(initialTarget);
                if (clampedTarget != initialTarget) _reservoirTarget.Set(clampedTarget);

                _reserveLiters = cfg.Define(
                    "ReserveLiters",
                    Ids.DefaultReserveLiters,
                    Section,
                    () => Text.T("megatank.cfg.reserve", "预留水量（升）", "Reserve amount (L)"),
                    tooltip: () => Text.T("megatank.cfg.reserve.tip",
                        "为水分收集器夜间产量预留的水量上限。0 = 关闭。湿气农场单台极限约 21.5 升/夜；自动模式会按当夜产量取更小的值。",
                        "Upper bound for the water kept free for the Moisture Farm's nightly output. 0 = off. The farm's hard limit is about 21.5 L/night; Auto mode reserves only what the next night needs."));
                _reserveLiters.OnChanged += OnReserveChanged;

                // 配置 JSON 可能被手改成越界值；加载后立即夹回，界面不会显示 999。
                int initialReserve = _reserveLiters.Value;
                int clampedReserve = ClampLiters(initialReserve);
                if (clampedReserve != initialReserve) _reserveLiters.Set(clampedReserve);

                _auto = cfg.Define(
                    "AutoReserve",
                    true,
                    Section,
                    () => Text.T("megatank.cfg.auto", "按当夜产量自动预留", "Auto reserve from output"),
                    tooltip: () => Text.T("megatank.cfg.auto.tip",
                        "开：按湿气农场当夜预期产量 + 0.5 升安全余量预留，主池不会长期空一截。关：固定使用上面的升数。",
                        "On: reserve the Moisture Farm's expected output plus a 0.5 L safety margin, so the tank does not stay visibly short. Off: use the fixed amount above."));

                _continuous = cfg.Define(
                    "ContinuousReserve",
                    false,
                    Section,
                    () => Text.T("megatank.cfg.continuous", "全天保持预留", "Keep reserve all day"),
                    tooltip: () => Text.T("megatank.cfg.continuous.tip",
                        "关：只在夜间结算前预腾，机器生产后回填主池，白天主池保持满。开：全天维持预留，主池会显示略少。",
                        "Off: reserve only before the night settles, then refill the tank after production, so it reads full during the day. On: keep the reserve all day; the tank will show slightly less."));

                _initialized = true;
                Log.Info("水量自动化配置已加载：Reservoir=" + _reservoirMode.Value
                         + " / Target=" + _reservoirTarget.Value + "%"
                         + " / ReserveLiters=" + _reserveLiters.Value
                         + " / Auto=" + _auto.Value + " / Continuous=" + _continuous.Value);
            }
            catch (Exception e)
            {
                Log.Once("r3-cfg", "水量自动化配置初始化失败（按默认机器预腾）：" + e.Message);
            }
        }

        private static void OnReservoirTargetChanged(int value)
        {
            int clamped = ClampPercent(value);
            if (clamped != value && _reservoirTarget != null) _reservoirTarget.Set(clamped);
        }

        private static int ClampPercent(int value)
        {
            if (value < Ids.ReservoirMinPercent) return Ids.ReservoirMinPercent;
            if (value > Ids.ReservoirMaxPercent) return Ids.ReservoirMaxPercent;
            return value;
        }

        /// <summary>数字输入框没有区间，手动把越界值写回配置（避免只读 getter 夹掉但界面仍显示 999）。</summary>
        private static void OnReserveChanged(int value)
        {
            int clamped = ClampLiters(value);
            if (clamped != value && _reserveLiters != null) _reserveLiters.Set(clamped);
        }

        private static int ClampLiters(int value)
        {
            if (value < 0) return 0;
            if (value > Ids.ReserveMaxLiters) return Ids.ReserveMaxLiters;
            return value;
        }

        internal static int ReserveLiters
        {
            get
            {
                int v = _reserveLiters != null ? _reserveLiters.Value : Ids.DefaultReserveLiters;
                return ClampLiters(v);
            }
        }

        internal static bool ReservoirMode => _reservoirMode != null && _reservoirMode.Value;

        internal static int ReservoirTargetPercent
        {
            get
            {
                int v = _reservoirTarget != null ? _reservoirTarget.Value : Ids.DefaultReservoirTargetPercent;
                return ClampPercent(v);
            }
        }

        internal static bool AutoReserve => _auto == null || _auto.Value;

        internal static bool Continuous => _continuous != null && _continuous.Value;
    }
}

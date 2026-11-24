// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// R3 机器预腾配置。必须在 IMod.Init（加载期）定义：
    /// 官方主菜单用 ModConfig.Find(modId) 判断有没有设置项。
    ///
    /// 三条默认值就是 0.14.0 的推荐行为：
    ///   · ReserveLiters = 25：夜间预留上限（普通 850 ml 产量会自动缩到约 1.35 L）；
    ///   · AutoReserve = true：按湿气农场当夜预期产量 + 0.5 L 安全余量预留；
    ///   · Continuous = false：只在夜间预腾，机器生产后回填主池，白天保持 100%。
    /// </summary>
    internal static class ReserveConfig
    {
        private static readonly string Section =
            CfgLocale.Sec("megatank.cfg.sec.water", "Water automation", "水量自动化");

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
                Log.Info("机器预腾配置已加载：ReserveLiters=" + _reserveLiters.Value
                         + " / Auto=" + _auto.Value + " / Continuous=" + _continuous.Value);
            }
            catch (Exception e)
            {
                Log.Once("r3-cfg", "机器预腾配置初始化失败（按默认 25 L / 自动 / 仅夜间）：" + e.Message);
            }
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

        internal static bool AutoReserve => _auto == null || _auto.Value;

        internal static bool Continuous => _continuous != null && _continuous.Value;
    }
}

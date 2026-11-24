// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// MegaTank 纯逻辑自检（P1b-07）
//
// 目的：把「改阈值 / 改公式时最容易静默改错」的几条锁住。
// 用法：dotnet run --project tests\PureCheck
// 退出码：0 = 全过；1 = 有用例失败（可直接进门禁）

using System;

namespace MegaTank.PureCheck
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            // ── 档位映射：0..100 → 0..10，边界必须落在整十 ──
            Check("Bucket(0)=0", WaterMath.Bucket(0, 10), 0);
            Check("Bucket(9)=0", WaterMath.Bucket(9, 10), 0);
            Check("Bucket(10)=1", WaterMath.Bucket(10, 10), 1);
            Check("Bucket(37)=3", WaterMath.Bucket(37, 10), 3);
            Check("Bucket(99)=9", WaterMath.Bucket(99, 10), 9);
            Check("Bucket(100)=10", WaterMath.Bucket(100, 10), 10);
            Check("Bucket(-5)=0", WaterMath.Bucket(-5, 10), 0);
            Check("Bucket(130)=10", WaterMath.Bucket(130, 10), 10);

            // ── 溢出量：请求量 − 实际进入量 ──
            // 主池还有 100，请求 500 → 进 100，溢出 400
            Check("Overflow(500,100,0)=400", WaterMath.Overflow(500, 100, 0), 400);
            // 装得下：请求 100，剩余 1000 → 进 100，溢出 0
            Check("Overflow(100,1000,900)=0", WaterMath.Overflow(100, 1000, 900), 0);
            // 主池已满（剩余 0），请求 500 → 进 0，溢出 500
            Check("Overflow(500,0,0)=500", WaterMath.Overflow(500, 0, 0), 500);
            // 剩余容量反而变大（异常数据）→ 不超过 0
            Check("Overflow(100,100,200)=0", WaterMath.Overflow(100, 100, 200), 0);
            // 请求为 0
            Check("Overflow(0,100,100)=0", WaterMath.Overflow(0, 100, 100), 0);

            // ── 该不该溢出（凭空造水的防线）──
            Check("ShouldSpill 正常溢出", WaterMath.ShouldSpill(500, 100, 0), true);
            Check("ShouldSpill 装得下", WaterMath.ShouldSpill(100, 1000, 900), true);
            Check("ShouldSpill 满池请求", WaterMath.ShouldSpill(500, 0, 0), true);
            Check("ShouldSpill 有余量却没进水=拒绝", WaterMath.ShouldSpill(500, 1000, 1000), false);
            Check("ShouldSpill 请求 0=拒绝", WaterMath.ShouldSpill(0, 100, 0), false);
            Check("ShouldSpill 剩余变大=拒绝", WaterMath.ShouldSpill(500, 100, 200), false);
            Check("ShouldSpill 过滤吃掉一部分=允许", WaterMath.ShouldSpill(500, 1000, 700), true);

            // ── 单次灌内胆的量 ──
            Check("SpillAdd(500,100)=100", WaterMath.SpillAdd(500, 100), 100);
            Check("SpillAdd(50,100)=50", WaterMath.SpillAdd(50, 100), 50);
            Check("SpillAdd(500,0)=0", WaterMath.SpillAdd(500, 0), 0);
            Check("SpillAdd(0,100)=0", WaterMath.SpillAdd(0, 100), 0);
            Check("SpillAdd(-5,100)=0", WaterMath.SpillAdd(-5, 100), 0);

            // ── 回补阈值：严格小于才补（90 不补，89 补）──
            Check("NeedsRefill(89,90)=true", WaterMath.NeedsRefill(89, 90), true);
            Check("NeedsRefill(90,90)=false", WaterMath.NeedsRefill(90, 90), false);
            Check("NeedsRefill(100,90)=false", WaterMath.NeedsRefill(100, 90), false);
            Check("NeedsRefill(0,90)=true", WaterMath.NeedsRefill(0, 90), true);

            // ── Clamp ──
            Check("Clamp(-1,0,10)=0", WaterMath.Clamp(-1, 0, 10), 0);
            Check("Clamp(11,0,10)=10", WaterMath.Clamp(11, 0, 10), 10);
            Check("Clamp(5,0,10)=5", WaterMath.Clamp(5, 0, 10), 5);

            // ── 容量阶梯（0.13.0 口径）：100 L 起步 / 每档 +100 L / 19 档满级 2000 L ──
            Check("CapacityAt(0)=100L", Ids.CapacityAt(0), 100000);
            Check("CapacityAt(1)=200L", Ids.CapacityAt(1), 200000);
            Check("CapacityAt(max)=2000L", Ids.CapacityAt(Ids.MaxExpandLevel), 2000000);
            Check("CapacityMl=2000L", Ids.CapacityMl, 2000000);
            Check("MaxExpandLevel=19", Ids.MaxExpandLevel, 19);
            Check("DefaultReserveLiters=25", Ids.DefaultReserveLiters, 25);
            Check("ReserveMaxLiters=50", Ids.ReserveMaxLiters, 50);
            Check("ReserveMarginParts=0.5L", Ids.ReserveMarginParts, 500000);
            Check("ReserveContinuousMaxPercent=8", Ids.ReserveContinuousMaxPercent, 8);
            Check("DefaultReservoirTargetPercent=90", Ids.DefaultReservoirTargetPercent, 90);
            Check("ReservoirBandPercent=5", Ids.ReservoirBandPercent, 5);
            Check("ReservoirMaxMoveParts=25L", Ids.ReservoirMaxMoveParts, 25000000);
            Check("LegacyLevelOffset=8", Ids.LegacyLevelOffset, 8);

            // ── 0.12.0 原版容器基础价：主池 350（water_tank）/ 内胆 25（water_pitcher）──
            Check("TankBasePrice=350", Ids.TankBasePrice, 350);
            Check("CellBasePrice=25", Ids.CellBasePrice, 25);
            // 老档迁移等价性：旧公式 900 + 100×L 必须等于新公式 100 + 100×(L+8)
            for (int lv = 0; lv <= 10; lv++)
            {
                Check("迁移 Lv." + lv + "→Lv." + (lv + Ids.LegacyLevelOffset),
                      Ids.CapacityAt(lv + Ids.LegacyLevelOffset),
                      Ids.LegacyBaseCapacityMl + lv * Ids.ExpandStepMl);
            }

            Console.WriteLine();
            Console.WriteLine("TOTAL passed=" + _passed + " failed=" + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(string name, int actual, int expected)
        {
            if (actual == expected) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL " + name + "  (actual=" + actual + ", expected=" + expected + ")");
        }

        private static void Check(string name, bool actual, bool expected)
        {
            if (actual == expected) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL " + name + "  (actual=" + actual + ", expected=" + expected + ")");
        }
    }
}

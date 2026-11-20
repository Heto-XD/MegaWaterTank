using System;
using HarmonyLib;

namespace MegaTank
{
    /// <summary>
    /// R1 溢出：主池装不下的部分，重现同参灌进内胆。
    ///
    /// ⚠️ 本补丁**不用 [HarmonyPatch] 特性**：
    ///   WaterHelper.AddWater 的方法体读 Liquid 静态字段，而 Liquid 的 cctor 会读本地化。
    ///   加载期 PatchAll（Awake 阶段，本地化未就绪）会把它打成永久损坏的类型
    ///   （历史上 DangpuPlus 补 WaterHelper.TransferLiquid 就是这么崩的）。
    ///   ⇒ 改由 LatePatches 在 ModHook.OnGameLoadedLate 之后手动安装。
    ///
    /// 为什么用「请求量 − 实际进入量」反算：
    ///   AddWater 内部自己做过 num5 = min(addAmount, free)，装不下的量被静默丢弃且不返回。
    /// 为什么 requested 不能也 min 一次：
    ///   若 requested = min(addAmount, freeBefore)，而 actual 天然 ≤ freeBefore，
    ///   overflow 恒为 0 → R1 永不触发（v4 初稿的 bug）。
    ///
    /// 已知边界：AddWater 内有 HandleFilter 分支，带滤芯的容器实际注入量 < 请求量。
    ///   主池不打 FILTER_CONTAINER_TAG，所以不触发。若将来给主池加滤芯，此反算必须重写。
    /// </summary>
    internal static class OverflowPatch
    {
        internal struct State
        {
            internal bool Active;
            internal int FreeBefore;
            internal int Requested;   // -1 = 加满语义，无溢出
        }

        internal static void Prefix(GameItem waterContainer, int addAmount, ref State __state)
        {
            // ★ Prefix 里绝不能抛：它嵌在 AddWater 内部，异常会穿到调用方
            //   （水龙头交互、机器灌装、过夜化学模拟的 for 循环），别人的结算会一起断。
            try
            {
                if (waterContainer == null) return;
                if (!waterContainer.IsTag(Ids.TankTag)) return;

                __state.Active = true;
                __state.FreeBefore = WaterHelper.GetFreeCapacity(waterContainer);
                __state.Requested = addAmount < 0 ? -1 : addAmount;
            }
            catch (Exception e)
            {
                __state.Active = false;
                Fault.Mark(Fault.Protection);
                Log.Once("r1-prefix", "R1 记录灌水前状态失败（本次不做溢出）：" + e.Message);
            }
        }

        internal static void Postfix(GameItem waterContainer, int grade, bool useRange, int lowPara, int highPara, State __state)
        {
            if (!__state.Active || __state.Requested < 0) return;

            try
            {
                int freeAfter = WaterHelper.GetFreeCapacity(waterContainer);
                if (!WaterMath.ShouldSpill(__state.Requested, __state.FreeBefore, freeAfter)) return;

                int overflow = WaterMath.Overflow(__state.Requested, __state.FreeBefore, freeAfter);
                if (overflow <= 0) return;

                TankRuntime.SpillToCells(waterContainer, grade, overflow, useRange, lowPara, highPara);
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Protection);
                Log.Once("r1", "R1 溢出失败（本次丢弃余量）：" + e.Message);
            }
        }
    }
}

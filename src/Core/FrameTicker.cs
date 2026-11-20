using System;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 自建帧循环：回补（R2）与档图巡检都挂在这里。
    ///
    /// 为什么不用 GameItem.onOneSecondItemFunc：
    ///   ChemicalHelper#RunNightlyChemicalSimulation 会对同一批物品循环 360 次，
    ///   回补会在过夜时跑 360 轮（实测日志可复现）。
    /// </summary>
    internal sealed class FrameTicker : MonoBehaviour
    {
        private static FrameTicker _instance;
        private float _last;

        internal static bool IsAlive => _instance != null;

        internal static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("MegaTank.FrameTicker");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<FrameTicker>();
            Log.Info("FrameTicker 已启动，周期 " + Ids.TickSeconds + "s");
        }

        internal static void Remove()
        {
            if (_instance == null) return;
            var go = _instance.gameObject;
            _instance = null;
            UnityEngine.Object.Destroy(go);
        }

        private void Update()
        {
            if (Time.unscaledTime - _last < Ids.TickSeconds) return;
            _last = Time.unscaledTime;

            try
            {
                TankRuntime.Tick();
            }
            catch (Exception e)
            {
                Log.Once("tick", "Tick 异常（本 tick 跳过）：" + e);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}

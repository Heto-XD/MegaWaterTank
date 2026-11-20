using System;
using HarmonyLib;

namespace MegaTank
{
    /// <summary>
    /// 存盘兜底：`PlayerStore#SaveGame` 前后把「电话名片」暂时摘下 / 放回，
    /// 让**存档里永远不出现** `megatank.phone` 这一条。
    ///
    /// 为什么必须这么做（实机 2026-11-20 两轮验证）：
    ///   1. 名片写在 `PlayerStore.PhoneClientDict`（public + 随存档序列化），和原版联系人同一机制
    ///      ⇒ 只要存盘那一刻它在字典里，就会被写进档。
    ///   2. "退出时删"这条路**走不通**：退出时序是
    ///      `mod enabled=False → ... → The mod loader object was destroyed`，
    ///      到 `OnDisable` 时 `PlayerStore.instance` 已经被清掉（`HasInstance` 为假），
    ///      而 `RemoveNetworkUpgrades` 只动静态表所以能成功——电话清不掉。
    ///   3. 于是玩家停用 mod 后会看到一条 `Translation Error 'megatank.phone' in ClientName`
    ///      （点开是 `PhoneDialogList.BugDialog()`）的幽灵联系人。
    ///
    /// 补丁形态（遵守根 AGENTS 硬规则 27：`PlayerStore#SaveGame` 是多 mod 共用热目标）：
    ///   · **只挂 Prefix + Finalizer**，不返回 false、不跳过原方法、不改任何存档字段；
    ///   · Prefix 记下号码并把条目移出字典，Finalizer **无论原方法成功/抛异常/被别的 mod 跳过**
    ///     都会把条目放回去（用 `Finalizer` 而不是 `Postfix`，正是为了覆盖"原方法抛异常"这条）。
    ///   · 嵌套调用用深度计数，只有最外层才摘/放。
    /// </summary>
    internal static class SaveStripPatch
    {
        private static int _depth;
        private static long _stashed;

        internal static void Prefix()
        {
            try
            {
                if (_depth > 0)
                {
                    _depth++;
                    return;
                }
                _depth = 1;
                _stashed = PhoneCard.DetachForSave();
            }
            catch (Exception e)
            {
                Log.Once("save-strip", "存盘前摘除名片失败（本条忽略，存档不受影响）：" + e.Message);
            }
        }

        internal static Exception Finalizer(Exception __exception)
        {
            try
            {
                if (_depth > 1)
                {
                    _depth--;
                    return __exception;
                }

                _depth = 0;
                long n = _stashed;
                _stashed = 0;
                PhoneCard.ReattachAfterSave(n);
            }
            catch (Exception e)
            {
                Log.Once("save-restore", "存盘后放回名片失败（下次进档会自愈）：" + e.Message);
            }

            return __exception;   // 原样返回，绝不吞掉原方法的异常
        }
    }
}

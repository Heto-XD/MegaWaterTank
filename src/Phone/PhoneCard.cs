using System;

namespace MegaTank
{
    /// <summary>
    /// 电话名片：把「核桃水商」写进电话簿，挂一个自定义拨号对话。
    ///
    /// 依据（与 ContainerMerchant 同构，已实机验证过的路径）：
    ///   · `PlayerStore.PhoneClientDict` 是 public 且序列化；键 = 号码(long)；
    ///   · `PhoneClientList.DialogFuncs` 是 public static Dictionary；
    ///   · 进快速拨号的条件是 `phoneState ∈ {Regular, Cooldown}` **且** `dialedBefore`。
    ///   · `locID` 不能留空，否则游戏去 ClientName 表查会显示 Translation Error；
    ///     所以写一个标记串，由 PhoneName 补丁在显示时替换成本地化文本。
    ///
    /// 号码：#21474836#（int.MaxValue 的前 8 位，梗是"数据上限"）。拨号盘只能输数字，
    /// 所以实际键是 21474836，带 # 的那串只出现在显示名里。
    /// </summary>
    internal static class PhoneCard
    {
        internal const string LocIdMarker = "megatank.phone";
        internal const string DialogId = "megatank_water_supplier";

        /// <summary>首选号码 + 撞号时依次退让（都保持 8 位以内）。</summary>
        private static readonly long[] Candidates = { 21474836L, 2147483L, 214748364L };

        private static long _number;
        private static bool _logged;

        internal static long Number => _number;

        /// <summary>幂等：读档(OnGameLoadedLate) / 新档(OnNewGameStarted) 各调用一次。已存在就修字段，不存在就补。</summary>
        internal static void Install()
        {
            try
            {
                var ps = PlayerStore.Instance;
                if (ps == null || ps.PhoneClientDict == null) return;

                long number = ResolveNumber(ps);
                if (number <= 0)
                {
                    Log.Once("phone-nonumber", "可选号码全被占用，电话名片未安装");
                    return;
                }

                _number = number;
                PhoneClientList.DialogFuncs[DialogId] = BuildDialog;

                StorePhoneClient client;
                if (!ps.PhoneClientDict.TryGetValue(number, out client) || client == null)
                {
                    client = new StorePhoneClient
                    {
                        // displayName 是兜底：万一改名补丁没挂上，玩家至少能看到名字
                        displayName = Text.T("megatank.phone.name", "核桃水商", "Hetao Water Co."),
                        locID = LocIdMarker,
                        dialogFuncId = DialogId,
                        phoneState = StorePhoneClient.PhoneState.Regular,
                        dialedBefore = true
                    };
                    ps.PhoneClientDict[number] = client;

                    if (!_logged)
                    {
                        _logged = true;
                        Log.Info("电话名片已写入，号码 " + number);
                    }
                    return;
                }

                // 老档：号码已在，但字段可能是旧的/被清过
                client.locID = LocIdMarker;
                client.dialogFuncId = DialogId;
                client.dialedBefore = true;
                if (client.phoneState == StorePhoneClient.PhoneState.NotResponding
                    || client.phoneState == StorePhoneClient.PhoneState.None)
                {
                    client.phoneState = StorePhoneClient.PhoneState.Regular;
                }
            }
            catch (Exception e)
            {
                Log.Once("phone-install", "电话名片写入失败：" + e.Message);
            }
        }

        /// <summary>挑一个没被别人占的号码；已经是我们自己的就复用（读档/换档都幂等）。</summary>
        private static long ResolveNumber(PlayerStore ps)
        {
            var dict = ps.PhoneClientDict;
            for (int i = 0; i < Candidates.Length; i++)
            {
                long candidate = Candidates[i];

                StorePhoneClient existing;
                if (!dict.TryGetValue(candidate, out existing) || existing == null) return candidate;

                // 已经是我们写的那条
                if (string.Equals(existing.locID, LocIdMarker, StringComparison.Ordinal)) return candidate;
            }

            return 0;
        }

        /// <summary>
        /// 停用时撤销：摘掉拨号对话 + 电话簿里我们自己那条。
        ///
        /// 为什么必须撤：`PhoneClientList.DialogFuncs` 是**全局静态字典**、
        /// `PlayerStore.PhoneClientDict` 是**随存档保存**的字典。只装不撤的话，
        /// 同进程内停用 mod 后电话簿仍留着这一条：
        ///   · 名字走 `LocHelper.GetLocalizedClientName("megatank.phone")`（我们的名字补丁已卸）
        ///     ⇒ 玩家看到的是键名/翻译错误；
        ///   · 点进去 `StorePhoneClient#GetCallDialog` 查不到 `dialogFuncId`
        ///     ⇒ 只能落到 `PhoneDialogList.BugDialog()`（"出 bug"对话）。
        /// 撤掉之后重开 mod 会自动重装（`Install` 幂等），购买记录与水无关，不丢东西。
        /// </summary>
        internal static void Uninstall()
        {
            try
            {
                PhoneClientList.DialogFuncs.Remove(DialogId);
            }
            catch (Exception e)
            {
                Log.Once("phone-uninstall-dialog", "摘除拨号对话失败：" + e.Message);
            }

            try
            {
                // 只有"店里已经加载过"才动 PlayerStore：`PlayerStore.Instance` 的 getter
                // 在实例为空时会**现场 new**（game-kb UPG-024），主菜单阶段构造会 NRE。
                // `EmporiumEntry.Instance` 是**不创建对象**的静态引用，用它做闸门。
                if (EmporiumEntry.Instance == null) return;

                var ps = PlayerStore.Instance;
                if (ps != null && ps.PhoneClientDict != null)
                {
                    long number = _number;
                    if (number <= 0) number = FindOwnNumber(ps);

                    StorePhoneClient client;
                    if (number > 0
                        && ps.PhoneClientDict.TryGetValue(number, out client)
                        && client != null
                        && string.Equals(client.locID, LocIdMarker, StringComparison.Ordinal))
                    {
                        ps.PhoneClientDict.Remove(number);
                        Log.Info("电话名片已移除，号码 " + number);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Once("phone-uninstall-card", "移除电话名片失败：" + e.Message);
            }
            finally
            {
                _number = 0;
                _logged = false;
            }
        }

        /// <summary>本局没走过 Install（_number=0）时，按标记串在电话簿里找自己那条。</summary>
        private static long FindOwnNumber(PlayerStore ps)
        {
            long found = 0;
            foreach (var kv in ps.PhoneClientDict)
            {
                var c = kv.Value;
                if (c != null && string.Equals(c.locID, LocIdMarker, StringComparison.Ordinal))
                {
                    found = kv.Key;   // 只可能有一条；多个候选号都写过也不会有第二条
                    break;
                }
            }
            return found;
        }

        private static Dialogue BuildDialog(StorePhoneClient caller)
        {
            var d = new Dialogue();
            try
            {
                string speaker = Text.T("megatank.phone.name", "核桃水商", "Hetao Water Co.");

                d.SetText(speaker, Text.T("megatank.phone.greeting",
                        "这里只卖水，不卖别的东西。",
                        "We sell water. Nothing else."))
                 .NextDialogue()
                 .SetText(speaker, Text.T("megatank.phone.offer",
                        "水箱和内胆都在单子上，买多少看你店的胃口。",
                        "Tanks and cells are both on the list. Buy as many as your store can swallow."))
                 .SetEndAction(OpenShop);
            }
            catch (Exception e)
            {
                Log.Once("phone-dialog", "对话构建失败：" + e.Message);
            }
            return d;
        }

        private static void OpenShop()
        {
            try
            {
                ModHelper.OpenWildNetwork();
            }
            catch (Exception e)
            {
                Log.Once("phone-open", "打开服务页失败：" + e.Message);
            }
        }
    }
}

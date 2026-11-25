// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

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
                RemoveStrays(ps, number);

                StorePhoneClient client;
                if (!ps.PhoneClientDict.TryGetValue(number, out client) || client == null)
                {
                    ps.PhoneClientDict[number] = MakeClient();

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
        /// 清掉"同一标记但号码不同"的残留（历史版本换号/异常退出可能留下）。
        /// 只删 `locID == 我方标记` 的，别人的电话一条都不碰。
        /// </summary>
        private static void RemoveStrays(PlayerStore ps, long keep)
        {
            try
            {
                var dict = ps.PhoneClientDict;
                if (dict == null) return;

                List<long> stale = null;
                foreach (var kv in dict)
                {
                    var c = kv.Value;
                    if (kv.Key != keep && c != null
                        && string.Equals(c.locID, LocIdMarker, StringComparison.Ordinal))
                    {
                        if (stale == null) stale = new List<long>();
                        stale.Add(kv.Key);
                    }
                }

                if (stale == null) return;
                for (int i = 0; i < stale.Count; i++) dict.Remove(stale[i]);
                Log.Info("清除电话名片残留 " + stale.Count + " 条（保留号码 " + keep + "）");
            }
            catch (Exception e)
            {
                Log.Once("phone-strays", "清除电话名片残留失败：" + e.Message);
            }
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
                // `PlayerStore.Instance` 的 getter 在实例为空时会**现场 new**（game-kb UPG-024），
                // 主菜单阶段构造会 NRE ⇒ 用 **`HasInstance`**（纯判空、不创建）当闸门。
                // 0.9.4 曾用 `EmporiumEntry.Instance == null` 当闸门——但退出游戏时场景先拆，
                // EmporiumEntry 已经没了 ⇒ 清理被整段跳过，幽灵名片留在存档里（实机 2026-11-20 复现）。
                if (!PlayerStore.HasInstance) return;

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

        /// <summary>名片对象：字段口径唯一出处在里，读档写入与存盘还原共用。</summary>
        private static StorePhoneClient MakeClient()
        {
            return new StorePhoneClient
            {
                // displayName 是兜底：万一改名补丁没挂上，玩家至少能看到名字
                displayName = Text.T("megatank.phone.name", "核桃水商", "Hetao Water Co."),
                locID = LocIdMarker,
                dialogFuncId = DialogId,
                phoneState = StorePhoneClient.PhoneState.Regular,
                dialedBefore = true
            };
        }

        /// <summary>
        /// 存盘前摘下名片（`SaveStripPatch` 的 Prefix 调用）。返回被摘掉的号码，0 = 本来就没有。
        ///
        /// 目的：让 `<see cref="PlayerStore.PhoneClientDict"/>` 在序列化那一刻**不含**本 mod 的条目
        /// ⇒ 存档里永远没有 `megatank.phone`，玩家停用 mod 后不会看到幽灵联系人。
        /// </summary>
        internal static long DetachForSave()
        {
            try
            {
                if (!PlayerStore.HasInstance) return 0;
                var ps = PlayerStore.Instance;
                if (ps == null || ps.PhoneClientDict == null) return 0;

                long number = _number > 0 ? _number : FindOwnNumber(ps);
                if (number <= 0) return 0;

                StorePhoneClient client;
                if (!ps.PhoneClientDict.TryGetValue(number, out client) || client == null) return 0;

                ps.PhoneClientDict.Remove(number);
                Log.Once("phone-strip",
                    "电话名片已在存盘时摘除（号码 " + number + "）：存档里不会留 megatank.phone 条目");
                return number;
            }
            catch (Exception e)
            {
                Log.Once("phone-detach", "存盘前摘除名片失败：" + e.Message);
                return 0;
            }
        }

        /// <summary>存盘后把名片放回（`SaveStripPatch` 的 Finalizer 调用，必须幂等）。</summary>
        internal static void ReattachAfterSave(long number)
        {
            try
            {
                if (number <= 0) return;
                if (!PlayerStore.HasInstance) return;

                var ps = PlayerStore.Instance;
                if (ps == null || ps.PhoneClientDict == null) return;
                if (ps.PhoneClientDict.ContainsKey(number)) return;   // 已经被谁放回去了

                ps.PhoneClientDict[number] = MakeClient();
                _number = number;
                PhoneClientList.DialogFuncs[DialogId] = BuildDialog;
            }
            catch (Exception e)
            {
                Log.Once("phone-reattach", "存盘后放回名片失败：" + e.Message);
            }
        }

        /// <summary>本局没走过 Install（_number=0）时，按标记串在电话簿里找自己那条。</summary>
        private static long FindOwnNumber(PlayerStore ps)        {
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

        /// <summary>
        /// 通话内容 = 一个自成一体的商城菜单（0.16.0 起不再打开原版「王尔德人脉」窗口）。
        ///
        /// 电话对话的三条硬约束（已核对 `DialoguePhoneUIManager` 1.0.0.16 源码）：
        ///   · **按钮文字来自「选项指向的那句对话」的正文**（`DisplayChoice` 读
        ///     `choices[i].Item2.plainText`），不是 item 名字 ⇒ 文案写进 label 节点，
        ///     买完刷新一次（`refreshLabels`），否则玩家看到的是上一轮状态。
        ///   · **选项指向的节点本身不会被朗读**：`SelectChoice` 把 `currentDialog` 设成它，
        ///     0.5 秒后 `ContinueConversation` 直接走到它的 `nextDialogue` ⇒ 结果句必须
        ///     在动作里 `label.NextDialogue().SetText(...)` 现造。
        ///   · **每行选项数受场景气泡数限制**（`dialogChoiceBubbles.Count`，超出直接不显示并报错）
        ///     ⇒ 商品固定 3 个；「结束通话」只在气泡 ≥ 4 时才加，否则靠手机本身的挂断键
        ///     （`PhoneUIManager.StopCall` 支持通话中挂断）。
        ///
        /// 买完回到菜单而不是挂断：扩容要买 19 次，来回拨号不现实。
        /// </summary>
        private static Dialogue BuildDialog(StorePhoneClient caller)
        {
            var d = new Dialogue();
            try
            {
                string speaker = Text.T("megatank.phone.name", "核桃水商", "Hetao Water Co.");

                // 菜单节点（带三个选项）+ 三个「只用来装按钮文字」的 label 节点
                var menu = new Dialogue();
                var labelTank = new Dialogue();
                var labelCell = new Dialogue();
                var labelExpand = new Dialogue();

                Action refreshLabels = () =>
                {
                    labelTank.SetText("PLAYER", Services.Label(ShopItem.Tank));
                    labelCell.SetText("PLAYER", Services.Label(ShopItem.Cell));
                    labelExpand.SetText("PLAYER", Services.Label(ShopItem.Expand));
                };
                refreshLabels();

                menu.SetText(speaker, Text.T("megatank.phone.offer",
                    "要什么直接说：水箱、内胆，还是扩容？",
                    "What will it be - a tank, a cell, or more capacity?"));

                AddItem(menu, labelTank, speaker, menu, ShopItem.Tank, refreshLabels);
                AddItem(menu, labelCell, speaker, menu, ShopItem.Cell, refreshLabels);
                AddItem(menu, labelExpand, speaker, menu, ShopItem.Expand, refreshLabels);

                int bubbles = ChoiceCapacity();
                if (bubbles >= 4)
                {
                    var labelBye = new Dialogue();
                    labelBye.SetText("PLAYER", Text.T("megatank.shop.hangup", "结束通话", "End the call"));
                    menu.AddChoice(ChoiceIcon(), "", "", labelBye, () =>
                    {
                        labelBye.NextDialogue()
                                .SetText(speaker, Text.T("megatank.phone.bye", "有需要再打。", "Call again if you need more."))
                                .SetEndAction(EndCall);
                    });
                }
                else
                {
                    // 气泡只有 3 个：不硬塞第四个（会被引擎丢弃并打 LogError），提示玩家用挂断键
                    Log.Info("电话选项气泡 " + bubbles + " 个，省略「结束通话」（点手机上的挂断键即可）");
                }

                // 首次通话先寒暄一句；快速拨号直接进菜单（与原版 PhoneWild 的行为一致）
                d.SetText(speaker, Text.T("megatank.phone.greeting",
                        "这里只卖水，不卖别的东西。",
                        "We sell water. Nothing else."))
                 .SetNextDialogue(menu);

                return IsQuickDial() ? menu : d;
            }
            catch (Exception e)
            {
                Log.Once("phone-dialog", "对话构建失败：" + e.Message);

                // ★ 必须给 title/text 兜底：`DialoguePhoneUIManager.StartDialogue` 第一句就是
                //   `currentDialog.title.Equals("PLAYER")`，title 为 null 会直接 NRE 把电话整条挂掉。
                d.SetText("Hetao Water Co.", "We sell water. Nothing else.");
            }
            return d;
        }

        /// <summary>
        /// 挂一个商品选项：点击 → 下单 → 把结果写进「结果句」，结果句再指回菜单。
        /// `choiceEnabled` 每次显示菜单都会重新求值（`Dialogue.IsDisabled`），所以按钮会随
        /// 余额/冷却/满级自动置灰；文字用 <paramref name="refresh"/> 现刷。
        /// </summary>
        private static void AddItem(Dialogue menu, Dialogue label, string speaker, Dialogue backTo,
                                    ShopItem item, Action refresh)
        {
            menu.AddChoice(ChoiceIcon(), "", "", label, () =>
            {
                string msg;
                try
                {
                    msg = Services.Buy(item);
                }
                catch (Exception e)
                {
                    Log.Once("phone-buy-" + item, "下单异常：" + e.Message);
                    msg = Text.T("megatank.shop.refuse.error",
                        "这单出岔子了，钱没动。把这条日志报给作者。",
                        "Something broke; you were not charged. Please report this log line.");
                }

                refresh();   // 让下一轮菜单显示正确的价格/「今天已买过」后缀
                label.NextDialogue().SetText(speaker, msg).SetNextDialogue(backTo);
            }, () => Services.CanBuy(item));
        }

        /// <summary>选项图标：原版所有电话选项都用 `$`（钱），这里保持一致。</summary>
        private static GameItem ChoiceIcon()
        {
            try
            {
                var icon = DirectoryMaster.Item("$");
                if (icon != null) return icon;
            }
            catch (Exception e)
            {
                Log.Once("phone-choice-icon", "取选项图标失败：" + e.Message);
            }
            Log.Once("phone-choice-icon-null", "取不到 `$` 选项图标，下单选项不会显示");
            return null;
        }

        /// <summary>当前场景给了几个选项气泡（商品 3 个是硬需求，第 4 个才轮到「结束通话」）。</summary>
        private static int ChoiceCapacity()
        {
            try
            {
                var ui = DialoguePhoneUIManager.Instance;
                if (ui != null && ui.dialogChoiceBubbles != null && ui.dialogChoiceBubbles.Count > 0)
                    return ui.dialogChoiceBubbles.Count;
            }
            catch (Exception e)
            {
                Log.Once("phone-bubbles", "读选项气泡数失败（按 3 处理）：" + e.Message);
            }
            return 3;
        }

        private static bool IsQuickDial()
        {
            try
            {
                var ui = PhoneUIManager.Instance;
                return ui != null && ui.isQuickDialCall;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void EndCall()
        {
            try
            {
                if (PhoneUIManager.Instance != null) PhoneUIManager.Instance.OnPhoneDialogEnd();
            }
            catch (Exception e)
            {
                Log.Once("phone-endcall", "结束通话失败：" + e.Message);
            }
        }
    }
}

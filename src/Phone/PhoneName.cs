// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// 电话簿显示名：`StorePhoneClient#GetLocalizedDisplayName` 只查游戏自己的 ClientName 表，
    /// 我们的 locID 不在其中 ⇒ 必须自己替换。
    ///
    /// 只在 `locID` 命中标记串时改写，其它联系人原样返回（不碰别人的名片）。
    /// 补丁由 LatePatches 在进档后安装（与本 mod 其它补丁同一个 owner）。
    /// </summary>
    internal static class PhoneName
    {
        internal static void Postfix(StorePhoneClient __instance, ref string __result)
        {
            try
            {
                if (__instance == null) return;
                if (!string.Equals(__instance.locID, PhoneCard.LocIdMarker, StringComparison.Ordinal)) return;

                __result = Text.T("megatank.phone.name", "核桃水商", "Hetao Water Co.");
            }
            catch (Exception e)
            {
                Log.Once("phone-name", "电话名替换失败：" + e.Message);
            }
        }
    }
}

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 机器单格槽尺寸策略（**默认不介入**）。
    ///
    /// ★ 0.10.2 起默认跟随原版：原版对任何尺寸的水容器都是
    ///   `GameSlotInventory.Validate()` 按 `currentItem.modifiedShape.globalWidth/Height × gridSize`
    ///   扩大槽位，再由 `MachineryPanel/Intake` 读 `widthPixels` 自适应窗口 ⇒ 小壶和大桶都能
    ///   "槽位变大 + 居中"。我们是 2×3 的水箱，理应与原版大水容器同待遇。
    ///
    /// 历史（0.6.1 → 0.10.1）：当时把槽位硬钉回 1×1 是为了压住"水培机窗口被撑成 691×414"。
    /// 但真正根因是 `TankSprites.ApplyBase` 用 `SetSpriteAndShapeFromMod` 把 shape 污染成
    /// 16×24（像素尺寸）——0.8.3 改 `SetSprite` 后 shape 已是正确的 2×3，钉回 1×1 就变成
    /// "多此一举且和原版不一致"：水箱在槽里被挤成一小团、位置也不居中（实机 2026-11-20 反馈）。
    ///
    /// 现在由配置 `Diagnostics → 机器槽按物品尺寸扩大` 控制：
    ///   · 开（默认）⇒ 本类直接 return，走原版逻辑；
    ///   · 关 ⇒ 钉回 1×1（旧行为），只在某些机器窗口被撑变形时当逃生阀。
    /// 只影响 MEGA_TANK_TAG / MEGA_CELL_TAG 的物品，不碰其它 mod 的槽位。
    /// </summary>
    internal static class MachineSlotPatch
    {
        private static readonly FieldInfo WidthField =
            AccessTools.Field(typeof(GameSlotInventory), "<widthPixels>k__BackingField");
        private static readonly FieldInfo HeightField =
            AccessTools.Field(typeof(GameSlotInventory), "<heightPixels>k__BackingField");

        internal static void Postfix(GameSlotInventory __instance)
        {
            try
            {
                if (__instance == null) return;
                if (DebugConfig.MachineSlotFollowsItem) return;   // 默认：跟随原版（槽位按 2×3 放大 + 居中）

                GameItem item = __instance.childItem;   // public getter，避免反射私有字段
                if (item == null) return;
                if (!item.IsTag(Ids.TankTag) && !item.IsTag(Ids.CellTag)) return;

                int grid = UISettings.current != null && UISettings.current.gridSize > 0
                    ? UISettings.current.gridSize : 16;

                if (WidthField != null) WidthField.SetValue(__instance, grid);
                if (HeightField != null) HeightField.SetValue(__instance, grid);

                // 同步可见的 RectTransform，否则背景还停在撑大后的尺寸
                ResizeChild(__instance, "background", grid);
                ResizeChild(__instance, "highlight", grid);
                ResizeChild(__instance, "insideBorder", grid + 2);
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("machineslot", "机器槽位尺寸修正失败（窗口可能仍偏大）：" + e.Message);
            }
        }

        private static void ResizeChild(GameSlotInventory inv, string name, int size)
        {
            // 原写法用 node.GetType() 取"运行时才算出的类型"，被官方扫描器判为
            // reflection-external（computed type）⇒ manifest 未声明 ⇒ 整个 mod 加载被拒。
            // 三个字段的实际类型就是 TreeNodeRender（GameSlotInventory.cs:16/18/26），
            // 用 as 直接转换即可，rectTransform 是它的 public 属性，无需任何类型反射。
            var node = AccessTools.Field(typeof(GameSlotInventory), name)?.GetValue(inv) as TreeNodeRender;
            if (node != null) node.rectTransform.sizeDelta = new Vector2(size, size);
        }
    }
}

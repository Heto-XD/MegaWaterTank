using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 把放了本 mod 物品的机器单格槽尺寸钉回 1×1。
    ///
    /// 原版 GameSlotInventory.Validate() 把单格槽的尺寸设成放入物品的
    /// modifiedShape.globalWidth × gridSize（源码 :253-254）。普通水壶 1×1 没事；
    /// 巨型水箱是 2×3 ⇒ 槽按 2×3 扩张 ⇒ MachineryIntake/MachineryPanel.Validate()
    /// 读 content.widthPixels ⇒ 整个机器窗口被撑大。
    ///
    /// 分工（0.8.3 厘清，别再把两件事混在一起）：
    ///   - **本类**只负责"2×3 的物品放进 1×1 槽时，槽不要跟着长到 2×3"。这是尺寸策略问题，
    ///     任何情况下都该钉回 1×1，与存档无关。
    ///   - **读档后窗口被撑到 691×414 的那种炸裂**是另一个 bug，根因在
    ///     TankSprites.ApplyBase 用 SetSpriteAndShapeFromMod 把 shape 污染成 16×24（像素尺寸），
    ///     已在 0.8.3 修掉（改用不碰 shape 的 SetSprite）。见该函数头注释与 LESSONS A7。
    ///   只靠本类压槽位是压不住上层缓存的，两处都得对。
    ///
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

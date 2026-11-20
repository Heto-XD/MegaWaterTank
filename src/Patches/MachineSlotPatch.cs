using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 修「读档后水培系统窗口被撑大」。
    ///
    /// 原版 GameSlotInventory.Validate() 把单格槽的尺寸设成放入物品的
    /// modifiedShape.globalWidth × gridSize（源码 :253-254）。普通水壶是 1×1 没事；
    /// 我们的巨型水箱是 2×3（且带 4×4 内容窗），放进机器的 1×1 水槽后 ⇒ 槽位按 2×3/4×4
    /// 扩张 ⇒ MachineryPanel.Validate() 读 content.widthPixels ⇒ 整个机器窗口被撑大。
    ///
    /// 修法：postfix 里若槽里是我们的物品，把槽尺寸钉回 1×1（含背景/highlight/border 的
    /// RectTransform），机器窗口就保持原版大小。物品仍能在槽里正常显示与被识别为水容器。
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

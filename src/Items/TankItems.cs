// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace MegaTank
{
    /// <summary>
    /// 物品定义。必须走 ModHook.OnModItemDirectoryInit，否则物品没有正式身份
    /// （实测表现：tooltip 显示「（未拥有）」、顾客拒收、DirectoryMaster.Item(id) 造不出来）。
    /// </summary>
    internal static class TankItems
    {
        internal static void Register(ModItemDirectory dir)
        {
            TankSprites.EnsureScaled();
            dir.Add(Ids.Tank, CreateTank);
            dir.Add(Ids.Cell, CreateCell);
            Log.Info("物品已注册：" + Ids.Tank + " / " + Ids.Cell + "（槽位 " + (Ids.SlotsW * Ids.SlotsH) + "）");
        }

        /// <summary>主池：2×3 格，16 个内胆槽。</summary>
        private static GameItem CreateTank()
        {
            var window = DirectoryUtils.CreateInventoryWindow(Ids.SlotsW, Ids.SlotsH);
            GameItem item = ItemDirectory.CreateEmptyItem(Ids.Tank);
            item.name = Text.T("megatank.item.tank.name", "巨型水箱", "Mega Water Tank");
            item.shortDescription = Text.T("megatank.item.tank.desc",
                "超大容量储水装置，可插入 16 个内胆扩容。里面装的是真实的水，能被水培机和净水设备识别。",
                "A very large water tank that takes up to 16 insert cells. The water inside is real: hydroponics and purifiers can read it.");
            item.unitValue = 0L;

            // 容量 / 水位 tag / 交互委托 / 化学意图，一次建好（isGraduated 让原版 tooltip 走 ml 显示）
            // 容量按当前扩容等级给（0.9.0）：新造的容器立刻是"起步 + 已购档位"的容量，
            // 不用等巡检校正。
            WaterHelper.InitLiquidContainerItem(
                item,
                capacityInMl: Capacity.CurrentMl,
                basePrice: Ids.TankBasePrice,   // 0.12.0：对齐原版 water_tank 的 350
                isFilterContainer: false,
                isGraduated: true,
                isDropper: false,
                isTransparentContainer: false);

            item.SetContentWindow(window.Item1);
            // 0.12.0：原版液体容器没有 CONTAINER_TAG（水箱/瓶子都能塞进任何装得下的容器）。
            // 主池有内容窗，用 CONTAINER_TOOLTIP_TAG 保留“容器”提示；嵌套不再被
            // CONTAINER_TAG + STORABLE_PACKAGE_TAG 限制在 STORAGE 类容器里。
            item.EnableTag("CONTAINER_TOOLTIP_TAG");
            item.EnableTag(Ids.TankTag);
            item.SetGameItemType("HOUSEHOLD_GOOD");  // 日用品类型，和子胆一致
            item.EnableTag("NOT_FOR_RESALE");        // 只留 tag：StoreClient#IsClientRefusingItem 第一行命中，但 tooltip 不再多一行类型标签
            AudioHelper.InitSolidMetal(item);        // 原版 water_tank 同款金属拖放音

            // 只有内胆能进槽
            ContainerHelper.AllowOnlyTaggedItemOwn(window.Item2, item, Ids.CellTag);

            TankSprites.ApplyBase(item);
            item.getItemSpriteModifierForItemFunc = TankLiquidLayer.Overlays;   // 水质着色层（方案 A）
            TankSprites.AttachLoadHook(item);   // 读档不恢复 useSpriteFromMod，必须补（否则图标变 ?）
            // 老档迁移：容器基础价 / NOT_FOR_RESALE 可见类型 / 旧的 CONTAINER_TAG 组合。
            // 读档时 state 与 itemTypes 会被存档覆盖，所以必须在 onLoaded 里再校正一次。
            item.onLoaded = (Action<GameItem>)Delegate.Combine(item.onLoaded, new Action<GameItem>(TankAttributes.OnLoaded));
            TankRegistry.Track(item);   // P0：进入本 mod 容器索引，Tick 不再全店扫描
            return item;
        }

        /// <summary>内胆：1×1 格，独立可用的完整水容器。</summary>
        private static GameItem CreateCell()
        {
            GameItem item = ItemDirectory.CreateEmptyItem(Ids.Cell);
            item.name = Text.T("megatank.item.cell.name", "水箱内胆", "Tank Cell");
            item.shortDescription = Text.T("megatank.item.cell.desc",
                "插进巨型水箱的槽位以扩容；也可以单独当水壶使用。",
                "Slots into a Mega Water Tank to expand it. Works as a water canister on its own too.");
            item.unitValue = 0L;

            WaterHelper.InitLiquidContainerItem(
                item,
                capacityInMl: Capacity.CurrentMl,
                basePrice: Ids.CellBasePrice,   // 0.12.0：对齐原版 water_pitcher 的 25
                isFilterContainer: false,
                isGraduated: true,
                isDropper: false,
                isTransparentContainer: false);

            item.EnableTag(Ids.CellTag);
            item.SetGameItemType("HOUSEHOLD_GOOD");  // 和主池一致：日用品类型
            item.EnableTag("NOT_FOR_RESALE");        // 只留 tag（见主池注释）
            AudioHelper.InitPlasticBottle(item);     // 0.12.0：小水罐，塑料瓶音

            TankSprites.ApplyBase(item);
            item.getItemSpriteModifierForItemFunc = TankLiquidLayer.Overlays;
            TankSprites.AttachLoadHook(item);
            item.onLoaded = (Action<GameItem>)Delegate.Combine(item.onLoaded, new Action<GameItem>(TankAttributes.OnLoaded));
            TankRegistry.Track(item);   // P0：内胆也入索引（水箱内容窗里的内胆同样由 GetAllItems 刷新兜底）
            return item;
        }

        // 说明：0.12.0 起不再需要 OnLoadedRetag —— 主池去掉了 CONTAINER_TAG，
        // 也就不再依赖“读档后补回 STORABLE_PACKAGE_TAG”。老档里的旧 tag 由
        // TankAttributes.OnLoaded 统一清掉。
    }
}

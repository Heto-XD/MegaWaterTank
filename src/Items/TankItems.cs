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

        /// <summary>主池：2×3 格，8 个内胆槽。</summary>
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
                basePrice: 0,
                isFilterContainer: false,
                isGraduated: true,
                isDropper: false,
                isTransparentContainer: false);

            item.SetContentWindow(window.Item1);
            item.EnableTag("CONTAINER_TAG");
            item.EnableTag(Ids.TankTag);
            item.EnableTag("STORABLE_PACKAGE_TAG");  // 带 CONTAINER_TAG 的物品要能塞进别的容器/储藏区，必须有这个（原版种子包同款）
            item.SetGameItemType("HOUSEHOLD_GOOD");  // 日用品类型，和子胆一致；叠加不覆盖 NOT_FOR_RESALE
            item.SetGameItemType("NOT_FOR_RESALE");  // 永不可售：StoreClient#IsClientRefusingItem 第一行命中

            // 只有内胆能进槽
            ContainerHelper.AllowOnlyTaggedItemOwn(window.Item2, item, Ids.CellTag);

            TankSprites.ApplyBase(item);
            item.getItemSpriteModifierForItemFunc = TankLiquidLayer.Overlays;   // 水质着色层（方案 A）
            TankSprites.AttachLoadHook(item);   // 读档不恢复 useSpriteFromMod，必须补（否则图标变 ?）
            // tag 不入存档：读档后 STORABLE_PACKAGE_TAG 会丢 ⇒ 水箱又塞不进容器。
            // 原版种子包就是 in onLoaded 里重新 EnableTag，照抄。
            item.onLoaded = (Action<GameItem>)Delegate.Combine(item.onLoaded, new Action<GameItem>(OnLoadedRetag));
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
                basePrice: 0,
                isFilterContainer: false,
                isGraduated: true,
                isDropper: false,
                isTransparentContainer: false);

            item.EnableTag(Ids.CellTag);
            item.SetGameItemType("HOUSEHOLD_GOOD");  // 和主池一致：日用品类型
            item.SetGameItemType("NOT_FOR_RESALE");

            TankSprites.ApplyBase(item);
            item.getItemSpriteModifierForItemFunc = TankLiquidLayer.Overlays;
            TankSprites.AttachLoadHook(item);
            TankRegistry.Track(item);   // P0：内胆也入索引（水箱内容窗里的内胆同样由 GetAllItems 刷新兜底）
            return item;
        }

        /// <summary>
        /// 读档后重设可嵌套 tag。tag 不进存档（SaveItemNode 只存 spritePath 等），
        /// 丢了 STORABLE_PACKAGE_TAG 水箱就又塞不进储藏区。原版种子包同款做法。
        /// 只动我们自己的物品，不碰别人的载入回调（Delegate.Combine 追加）。
        /// </summary>
        private static void OnLoadedRetag(GameItem item)
        {
            if (item == null) return;
            item.EnableTag("STORABLE_PACKAGE_TAG");
        }
    }
}

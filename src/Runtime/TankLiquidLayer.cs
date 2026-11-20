using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 水质着色层（方案 A，见 docs\可行性-水质贴图着色.md）。
    ///
    /// 完全复用原版机制：<c>getItemSpriteModifierForItemFunc</c> 返回带 <c>color</c> 的
    /// <see cref="ItemSpriteModifier"/>，由 <c>GameItemElement.UpdateInnateModSprite</c> 用
    /// `image.color` 乘法染色。颜色来自原版 <c>WaterHelper.GetLiquidContainerColor</c>（按体积×颜色倍率加权平均），
    /// 所以净水/地沟水/锈水/任何化学品、任何 mod 的液体，颜色都与原版水瓶**一致**。
    ///
    /// 水层是灰度 mask（自带竖向明暗 + 左高光），染色后保留明暗层次。
    /// </summary>
    internal static class TankLiquidLayer
    {
        /// <summary>
        /// 供物品委托调用。空罐返回 null（不叠水层，与原版一致）。
        /// 任何异常只报首次并返回 null——最坏只是水不显色，不影响灌水。
        /// </summary>
        internal static List<ItemSpriteModifier> Overlays(GameItem item)
        {
            try
            {
                if (item == null) return null;
                bool isTank = item.IsTag(Ids.TankTag);
                bool isCell = item.IsTag(Ids.CellTag);
                if (!isTank && !isCell) return null;

                int pct = WaterHelper.GetCurrentCapacity(item);
                if (pct <= 0) return null;

                int bucket = WaterMath.Bucket(pct, Ids.MaxBucket);
                string id = isTank ? Ids.Tank : Ids.Cell;

                Sprite mask = TankSprites.Get(TankSprites.LiquidKey(id, bucket));
                if (mask == null) return null;

                string hex = WaterHelper.GetLiquidContainerColor(item);
                Color tint = MetaHelper.HexToColor(hex);

                // 两层（对齐原版 WaterHelper.InitLiquidContainerItem 的写法，0.11.0）：
                //   ① 水体：灰度 mask × 水质色（`color` 会被 image.color 乘上去）
                //   ② 高光：**不带颜色**（color 留 default/白），玻璃反光与液面亮点保持白色，
                //      不会被水色染成"亮色条"——单层版本就是因此显得扁平、横纹像条形码。
                var list = new List<ItemSpriteModifier>(2)
                {
                    new ItemSpriteModifier(mask, Vector2.zero, 0, false, 0, false, tint)
                };

                Sprite hl = TankSprites.Get(TankSprites.HighlightKey(id, bucket));
                if (hl != null)
                {
                    list.Add(new ItemSpriteModifier(hl, Vector2.zero));
                }

                return list;
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("liquid-layer", "水层构建失败（本次不显色）：" + e.Message);
                return null;
            }
        }
    }
}

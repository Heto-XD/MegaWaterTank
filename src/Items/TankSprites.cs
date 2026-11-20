using System;
using System.Collections.Generic;
using UnityEngine;

namespace MegaTank
{
    /// <summary>
    /// 贴图库（分层方案）：
    ///   基础图 = 空瓶（框 + 玻璃，不含水），物品 spritePath 用它；
    ///   水层 = 灰度 mask，运行时由 <see cref="TankLiquidLayer"/> 用水质平均色染色叠加。
    ///
    /// 硬规则（P0 实测）：
    ///   1. Textures/Items 的图只进 mod.Sprites[纯文件名]；
    ///   2. ppu = 图片素宽 ÷ (格数 × gridSize) × 100，gridSize 恒 16；
    ///      水层要和基础图同 ppu，否则叠加时错位/放大。
    /// </summary>
    internal static class TankSprites
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        /// <summary>空瓶基础图键：mega_water_tank / mega_water_cell。</summary>
        internal static string BaseKey(string itemId)
        {
            return itemId;
        }

        /// <summary>水层键：mega_water_tank_liquid_070 …</summary>
        internal static string LiquidKey(string itemId, int bucket)
        {
            return itemId + "_liquid_" + (bucket * 10).ToString("000");
        }

        private static LoadedMod Self()
        {
            var mods = ModLoader.Mods;
            if (mods == null) return null;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m != null && m.Manifest != null && m.Manifest.ID == Ids.ModId) return m;
            }
            return null;
        }

        private static int GridSize()
        {
            try
            {
                if (UISettings.current != null && UISettings.current.gridSize > 0)
                    return UISettings.current.gridSize;
            }
            catch (Exception e)
            {
                Fault.Mark(Fault.Cosmetic);
                Log.Once("gridsize", "读 gridSize 失败，按 16 处理：" + e.Message);
            }
            return 16;
        }

        private static int CellsWide(string itemId)
        {
            return itemId == Ids.Tank ? Ids.TankW : Ids.CellW;
        }

        /// <summary>
        /// 旧存档的 spritePath 键名（0.4.1 及之前的单图档位图）。
        /// 这些图在 v0.5.0 分层方案里已删除，但存档里的 spritePath 不会自动改。
        /// 保留别名指向新基础图，避免 ResolveSpriteByName 查不到 → 回退 dummy sprite
        /// （dummy 的 ppu 与占格都不受我们控制，表现就是"水箱显示得极大"）。
        /// </summary>
        private static readonly string[] LegacyKeys =
        {
            "mega_water_tank_4x", "mega_water_cell_4x"
        };

        /// <summary>旧键 → 新基础图键。</summary>
        private static string CanonicalKey(string key)
        {
            if (key == LegacyKeys[0]) return Ids.Tank;
            if (key == LegacyKeys[1]) return Ids.Cell;
            return key;
        }

        /// <summary>
        /// 把已加载的 Sprite 换成自定义 ppu 的版本（按物品格宽换算）。
        /// ★ 必须写回 mod.Sprices[key]：ResolveSpriteByName 从 mod.Sprites 取 background sprite，
        ///   只写自己的 Cache 会导致"水层按 ppu=800 显示、背景按 ppu=100 显示"尺寸错配。
        /// </summary>
        private static bool Rescale(LoadedMod mod, string key, int cellsW)
        {
            Sprite old;
            if (mod.Sprites == null || !mod.Sprites.TryGetValue(key, out old) || old == null) return false;
            Texture2D tex = old.texture;
            if (tex == null) return false;

            float ppu = tex.width / (float)(cellsW * GridSize()) * 100f;
            var sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
            if (sp == null) return false;

            Cache[key] = sp;          // 水层 Get() 走这里（ItemSpriteModifier 直接传 sprite 对象）
            mod.Sprites[key] = sp;    // ★ background 走 ResolveSpriteByName → mod.Sprites[key]
            return true;
        }

        /// <summary>注册期调用一次：给基础图 + 全部水层做 ppu 校准，并缓存。</summary>
        internal static void EnsureScaled()
        {
            var mod = Self();
            if (mod == null) { Log.Once("self", "Self() == null，贴图未校准"); return; }

            int ok = 0, tried = 0;
            string[] items = { Ids.Tank, Ids.Cell };
            for (int i = 0; i < items.Length; i++)
            {
                tried++;
                if (Rescale(mod, BaseKey(items[i]), CellsWide(items[i]))) ok++;

                for (int b = 0; b <= Ids.MaxBucket; b++)
                {
                    tried++;
                    if (Rescale(mod, LiquidKey(items[i], b), CellsWide(items[i]))) ok++;
                }
            }

            // 旧存档兼容：spritePath 可能是已删除的 _4x 键。
            // 把新基础图的校准版挂到旧键上，ResolveSpriteByName 就能命中，
            // 不再回退 dummy sprite（dummy 尺寸不受控 = 水箱显示巨大）。
            for (int i = 0; i < LegacyKeys.Length; i++)
            {
                string canonical = CanonicalKey(LegacyKeys[i]);
                Sprite sprite;
                if (mod.Sprites.TryGetValue(canonical, out sprite) && sprite != null)
                {
                    mod.Sprites[LegacyKeys[i]] = sprite;
                    Cache[LegacyKeys[i]] = sprite;
                    Log.Info("旧 sprite 键兼容：" + LegacyKeys[i] + " → " + canonical);
                }
            }

            Log.Info("贴图校准 " + ok + "/" + tried + "（基础图 + 灰度水层）");
        }

        /// <summary>取校准后的贴图；没有返回 null（调用方降级：不叠水层）。</summary>
        internal static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Sprite s;
            if (Cache.TryGetValue(key, out s)) return s;
            return Cache.TryGetValue(CanonicalKey(key), out s) ? s : null;
        }

        /// <summary>物品的 spritePath 是否还是旧键（巡检据此强制重贴）。</summary>
        internal static bool IsLegacySpritePath(GameItem item)
        {
            if (item == null) return false;
            string path = item.spritePath;
            if (string.IsNullOrEmpty(path)) return false;
            for (int i = 0; i < LegacyKeys.Length; i++)
                if (path == LegacyKeys[i]) return true;
            return false;
        }

        /// <summary>
        /// 给物品设置空瓶基础图 + 固定占格。
        /// 顺序不能反：SetSpriteAndShapeFromMod 写 spritePath + 置 spriteChanged，再 SetShape 覆盖格数。
        /// </summary>
        internal static void ApplyBase(GameItem item)
        {
            if (item == null || item.identifier == null) return;

            string key = BaseKey(item.identifier);
            if (Get(key) == null)
            {
                Log.Once("sprite-miss-" + item.identifier, "基础图缺失：" + key);
                return;
            }

            item.useSpriteFromMod = true;   // 读档不恢复此标记，必须显式补（否则图标 ?）
            item.SetSpriteAndShapeFromMod(Ids.ModId, key);
            item.SetShape(item.identifier == Ids.Tank
                ? new GridShapeBuilder().SetDataFill(Ids.TankW, Ids.TankH)
                : new GridShapeBuilder().SetDataFill(Ids.CellW, Ids.CellH));
        }

        /// <summary>
        /// 挂读档回调：补回 useSpriteFromMod + 基础图。
        /// 官方在 PlayerStore 读档链末尾逐物品调 onLoaded（此时 state 已恢复）。
        /// Delegate.Combine 追加，不覆盖原版可能挂的回调。
        /// </summary>
        internal static void AttachLoadHook(GameItem item)
        {
            if (item == null) return;
            item.onLoaded = (Action<GameItem>)Delegate.Combine(item.onLoaded, new Action<GameItem>(OnLoaded));
        }

        private static void OnLoaded(GameItem item)
        {
            try
            {
                ApplyBase(item);
                // 读档若绕过了物品工厂，委托可能没挂上；补一道，确保水层持续工作
                if (item.getItemSpriteModifierForItemFunc == null)
                    item.getItemSpriteModifierForItemFunc = TankLiquidLayer.Overlays;
            }
            catch (Exception e)
            {
                Log.Once("onload-" + (item != null ? item.identifier : "?"), "读档后重贴图失败：" + e.Message);
            }
        }
    }
}

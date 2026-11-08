using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Map.Editor
{
    /// <summary>编辑器里画瓦片 / 色块的公共小工具（只有 IMGUI 原语，不含业务判断）。</summary>
    internal static class MapEditorGUI
    {
        /// <summary>
        /// 瓦片在贴图里的 UV 区域。
        /// 用 <see cref="Sprite.rect"/> 而不是 <c>textureRect</c>：后者在 tight packing 下会抛异常。
        /// </summary>
        public static Rect SpriteCoords(Sprite sprite)
        {
            var tex = sprite.texture;
            if (tex == null) return new Rect(0f, 0f, 1f, 1f);
            var r = sprite.rect;
            float tw = tex.width;
            float th = tex.height;
            return new Rect(r.x / tw, r.y / th, r.width / tw, r.height / th);
        }

        /// <summary>按乘色画一张 Sprite（<paramref name="area"/> 是 GUI 空间矩形，y 向下）。</summary>
        public static void DrawSprite(Rect area, Sprite sprite, Color tint)
        {
            if (sprite == null) return;
            var tex = sprite.texture;
            if (tex == null) return;
            var prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(area, tex, SpriteCoords(sprite), true);
            GUI.color = prev;
        }

        /// <summary>画一层半透明纯色（地图外框、网格线、选中高亮都用它）。</summary>
        public static void Fill(Rect area, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        /// <summary>只画矩形边框（<paramref name="thickness"/> 是线宽，像素）。</summary>
        public static void Outline(Rect area, Color color, float thickness = 1f)
        {
            Fill(new Rect(area.x, area.y, area.width, thickness), color);
            Fill(new Rect(area.x, area.yMax - thickness, area.width, thickness), color);
            Fill(new Rect(area.x, area.y, thickness, area.height), color);
            Fill(new Rect(area.xMax - thickness, area.y, thickness, area.height), color);
        }

        /// <summary>
        /// 没配图时的占位块：材质色半透明 + 描边 + 中心小点。
        /// <para>
        /// 不画花纹是故意的——占位必须和真瓦片一眼能分开，否则「以为配好了其实没配」。
        /// </para>
        /// </summary>
        public static void DrawPlaceholder(Rect area, Color color)
        {
            var fill = color;
            fill.a = 0.5f;
            Fill(area, fill);
            Outline(area, new Color(0f, 0f, 0f, 0.45f), 1f);

            float s = Mathf.Clamp(area.width * 0.16f, 2f, 6f);
            Fill(new Rect(area.center.x - s * 0.5f, area.center.y - s * 0.5f, s, s),
                new Color(0f, 0f, 0f, 0.35f));
        }

        /// <summary>把 <paramref name="area"/> 画成缩略图：有图就画图，没图画材质色占位。</summary>
        public static void DrawThumb(Rect area, Sprite sprite, Color fallback)
        {
            Fill(area, new Color(0.17f, 0.17f, 0.18f, 1f));
            if (sprite != null) DrawSprite(area, sprite, Color.white);
            else DrawPlaceholder(area, fallback);
            Outline(area, new Color(0f, 0f, 0f, 0.6f), 1f);
        }
    }

    /// <summary>编辑器侧对瓦片集的写操作（统一处理去重，改完由调用方 SetDirty）。</summary>
    internal static class MapEditorUtil
    {
        /// <summary>某个 Sprite 在瓦片池里的下标，没有返回 -1。</summary>
        public static int IndexOfTile(MapTileSet set, Sprite sprite)
        {
            if (set == null || sprite == null || set.tiles == null) return -1;
            for (int i = 0; i < set.tiles.Count; i++)
                if (set.tiles[i] == sprite) return i;
            return -1;
        }

        /// <summary>
        /// 取某 Sprite 在瓦片池里的下标，没有就追加。
        /// <para>
        /// 向导批量生成规则走这里，所以同一个 Sprite 被多次引用不会在池里出现多份。
        /// 不做 <c>SetDirty</c>：一次向导会追加十几张，让调用方最后统一提交一次。
        /// </para>
        /// </summary>
        public static int EnsureTile(MapTileSet set, Sprite sprite)
        {
            if (set == null || sprite == null) return -1;
            int i = IndexOfTile(set, sprite);
            if (i >= 0) return i;
            set.tiles.Add(sprite);
            return set.tiles.Count - 1;
        }

        /// <summary>
        /// 把 Project 窗口当前选中的 Sprite 批量加进瓦片池，返回新加了几张。
        /// 选中子资产（贴图里的单个 Sprite）与整张贴图都认。
        /// </summary>
        public static int AddSelectedSprites(MapTileSet set)
        {
            if (set == null) return 0;
            var objs = Selection.objects;
            if (objs == null || objs.Length == 0) return 0;

            int added = 0;
            for (int i = 0; i < objs.Length; i++)
            {
                var sp = objs[i] as Sprite;
                if (sp == null) continue;
                if (IndexOfTile(set, sp) >= 0) continue;
                set.tiles.Add(sp);
                added++;
            }
            return added;
        }

        /// <summary>某层值下拉用的标签（<paramref name="includeEmpty"/> 时第 0 项留给「空」）。</summary>
        public static string[] ValueLabels(MapTileSet set, int grid, bool includeEmpty)
        {
            var list = set != null ? set.ValuesOf(grid) : null;
            int count = list != null ? list.Count : 0;
            if (count == 0) return includeEmpty ? new[] { "空" } : new string[0];

            int n = count + (includeEmpty ? 1 : 0);
            var labels = new string[n];
            int off = 0;
            if (includeEmpty)
            {
                labels[0] = "空";
                off = 1;
            }
            for (int i = 0; i < count; i++)
            {
                var m = list[i];
                labels[i + off] = m != null
                    ? $"{m.displayName}（{m.key}）"
                    : $"(空槽 {i})";
            }
            return labels;
        }

        /// <summary>某层值下标 → 颜色，空槽给灰色。</summary>
        public static Color ColorOf(MapTileSet set, int grid, int valueIndex)
        {
            var list = set != null ? set.ValuesOf(grid) : null;
            var m = list != null && (uint)valueIndex < (uint)list.Count ? list[valueIndex] : null;
            return m != null ? m.color : new Color(0.5f, 0.5f, 0.5f, 1f);
        }
    }
}

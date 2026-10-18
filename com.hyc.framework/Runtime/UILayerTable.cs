using System.Collections.Generic;
using HYC.Framework.Runtime;

namespace HYC.Framework.UI
{
    /// <summary>
    /// 一层 UI 的定义。层表由 <see cref="FrameworkSettings.uiLayers"/> 持久化（Resources 资产，
    /// 随工程进版本控制），在 HYC Settings → UI/层级 里增删改名、拖动排序。
    ///
    /// <para><b>顺序即层级</b>：下标 0 在最底层（HUD），越往后越靠上——决定 sortingOrder
    /// 与 Hierarchy 里的兄弟顺序。</para>
    ///
    /// <para><b>三个群体行为标记</b>：替代原先在 <see cref="UIManager"/> 里按层名写死的判断，
    /// 改名 / 插层后行为自动跟着层走（不跟名字走）：</para>
    /// <list type="bullet">
    /// <item><see cref="hud"/>：有窗口类层开着时，本层的 Canvas 与射线被自动关闭（详见 UIManager.ResetHudVisible）。</item>
    /// <item><see cref="blockInput"/>：本层窗口会挡住世界空间的输入（HitTest）。</item>
    /// <item><see cref="hideOnHideAll"/>：HideAllUI 时是否一起隐藏（不勾的层 = 常驻，如 Loading / Cursor / Mask）。</item>
    /// </list>
    /// </summary>
    [System.Serializable]
    public sealed class UILayerDef
    {
        /// <summary>层名：Hierarchy 里的节点名，也是生成常量 <c>UILayer.{name}</c> 的名字。须为合法 C# 标识符。</summary>
        public string name = "";

        /// <summary>中文描述。只写进资产与设置页提示，不进生成代码。</summary>
        public string desc = "";

        /// <summary>属于 HUD 组：有窗口类层开着时自动隐藏（默认 HUD / HUDNotice 为 true）。</summary>
        public bool hud;

        /// <summary>挡住世界空间输入（默认 Loading / Dialog / Window 为 true）。</summary>
        public bool blockInput;

        /// <summary>HideAllUI 时一起隐藏（默认 true；Loading / Cursor / Mask 为 false = 常驻）。</summary>
        public bool hideOnHideAll = true;
    }

    /// <summary>
    /// UI 层表的运行期访问入口（<see cref="FrameworkSettings.uiLayers"/> 的门面）。
    ///
    /// <para><b>为什么不是枚举</b>：框架的公开 API（<c>UIManager.Open&lt;T&gt;(layer, …)</c>）一旦引用
    /// 游戏侧生成的类型就会形成程序集环（游戏引用框架 + 框架引用游戏）；层集合本是项目数据，
    /// 做成资产后改层不需要改框架、不需要发包。需要编译期常量时，用设置页「保存并生成」产出的
    /// <c>UILayer</c> 静态类（int 常量，零耦合）。</para>
    /// </summary>
    public static class UILayerTable
    {
        /// <summary>默认层名表。顺序即层级；与旧版私有枚举 <c>WindowLayer</c> 一一对应。</summary>
        private static readonly string[] DefaultNames =
        {
            "HUD", "HUDNotice", "Window", "WindowNotice", "Dialog",
            "TopWindow", "ToolTip", "Loading", "Cursor", "Mask",
        };

        /// <summary>默认层表（资产缺失 / 旧资产无此字段时的内存兜底）。结构即时生效，落盘由编辑器负责。</summary>
        public static List<UILayerDef> DefaultTable()
        {
            return new List<UILayerDef>
            {
                new UILayerDef { name = "HUD",         desc = "血条 / 小地图等常驻信息", hud = true },
                new UILayerDef { name = "HUDNotice",   desc = "飘字 / 拾取提示",          hud = true },
                new UILayerDef { name = "Window",      desc = "主功能窗口（背包 / 任务）",  blockInput = true, hideOnHideAll = false },
                new UILayerDef { name = "WindowNotice",desc = "窗口之上的轻提示",          hideOnHideAll = false },
                new UILayerDef { name = "Dialog",      desc = "需要玩家决策的模态框",      blockInput = true, hideOnHideAll = false },
                new UILayerDef { name = "TopWindow",   desc = "压在弹窗之上的窗口",        hideOnHideAll = false },
                new UILayerDef { name = "ToolTip",     desc = "跟随鼠标的说明",            hideOnHideAll = false },
                new UILayerDef { name = "Loading",     desc = "加载遮罩",                blockInput = true, hideOnHideAll = false },
                new UILayerDef { name = "Cursor",      desc = "自定义鼠标指针",          hideOnHideAll = false },
                new UILayerDef { name = "Mask",        desc = "全屏遮挡（引导 / 截屏）", hideOnHideAll = false },
            };
        }

        /// <summary>
        /// 当前生效的层表。永不为 null：旧版 FrameworkSettings 资产没有此字段时就地填默认表
        /// （只改内存，不落盘——编辑器保存设置时才会写进资产）。
        /// </summary>
        public static List<UILayerDef> All
        {
            get
            {
                var settings = FrameworkSettings.Instance;
                if (settings.uiLayers == null || settings.uiLayers.Count == 0)
                    settings.uiLayers = DefaultTable();
                return settings.uiLayers;
            }
        }

        /// <summary>层数。列表元素个数，与 LayerNodes / LayerViews 的长度一致。</summary>
        public static int Count => All.Count;

        /// <summary>层名 → 下标；找不到返回 -1（比较运算里 -1 永不等于合法下标，可安全用于相等判断）。</summary>
        public static int Index(string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            var all = All;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].name == name)
                    return i;
            }
            return -1;
        }

        /// <summary>该层名是否已在表中。</summary>
        public static bool Contains(string name) => Index(name) >= 0;

        /// <summary>下标 → 层名；越界返回 null。</summary>
        public static string Name(int layer)
        {
            var all = All;
            return layer >= 0 && layer < all.Count ? all[layer]?.name : null;
        }

        /// <summary>该层是否属于 HUD 组（越界 false）。</summary>
        public static bool IsHud(int layer)
        {
            var all = All;
            return layer >= 0 && layer < all.Count && all[layer] != null && all[layer].hud;
        }

        /// <summary>该层是否挡住世界空间输入（越界 false）。</summary>
        public static bool BlocksInput(int layer)
        {
            var all = All;
            return layer >= 0 && layer < all.Count && all[layer] != null && all[layer].blockInput;
        }

        /// <summary>HideAllUI 时该层是否一起隐藏（越界 false，即"不动它"）。</summary>
        public static bool HiddenOnHideAll(int layer)
        {
            var all = All;
            return layer >= 0 && layer < all.Count && all[layer] != null && all[layer].hideOnHideAll;
        }

        /// <summary>层表里缺失的默认层名（顺序保持默认表顺序）。为空表示默认层名齐全。</summary>
        public static List<string> MissingDefaultNames()
        {
            var missing = new List<string>();
            foreach (var name in DefaultNames)
            {
                if (Index(name) < 0)
                    missing.Add(name);
            }
            return missing;
        }
    }
}

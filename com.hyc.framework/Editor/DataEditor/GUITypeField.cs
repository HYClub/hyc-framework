using System;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 单个字段的编辑器元信息：由 <see cref="GUIType"/> 反射构建，
    /// 承载分组、可见性、范围、只读等绘制所需数据。
    /// </summary>
    public class GUITypeField
    {
        public string Name;
        public Type Type;
        public int Level;    // 继承层级（基类字段层级更大）
        public int Index;    // 同层级内声明顺序
        public GUIContent Label;
        public bool Multiple;               // 多行字符串
        public bool HideInInspector;
        public VisibleAttribute Visible;
        public MinAttribute Min;
        public MaxAttribute Max;
        public int SpaceAtEnd;
        public bool ReadOnly;
        public bool Line;
        public bool FlatDisplay;
        public bool IsLocKey;
        public bool IsBehaviourTree;
        public bool IsAddressable;
        /// <summary>字段带 [AudioField]，绘制音频下拉（框架/音频 类型）。</summary>
        public bool IsAudioField;
        /// <summary>
        /// [Header("…")] 的文本（生成代码里来自模板的"显示名字"）。
        /// Unity 只在默认 PropertyField 流程里画 Header，走自定义绘制分支时必须自己画，
        /// 否则行为树/音频/多语言Key 这些字段在数据配置面板上会丢掉描述。
        /// </summary>
        public string Header;
        public InfoBoxAttribute[] Infos = Array.Empty<InfoBoxAttribute>();
        /// <summary>
        /// [CfgVisibleWhen] 条件可见性（模板字段「关联参数」生成）：全部满足才绘制；
        /// 目标字段缺失/类型不符视为满足（保持显示）。空数组 = 无条件。
        /// </summary>
        public CfgVisibleWhenAttribute[] VisibleWhen = Array.Empty<CfgVisibleWhenAttribute>();
    }
}

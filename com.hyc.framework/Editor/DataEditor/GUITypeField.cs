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
        public InfoBoxAttribute[] Infos = Array.Empty<InfoBoxAttribute>();
    }
}

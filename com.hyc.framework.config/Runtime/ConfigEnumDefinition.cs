using System;
using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>
    /// 枚举定义：用户填写值名称与描述，数值由程序自动分配。
    /// 普通枚举 = 1,2,3,4...；复合枚举（Flags）= 1,2,4,8,16...。
    /// 生成 C# 枚举代码，并按导出目标生成到客户端/服务器目录。
    /// </summary>
    [CfgAsset("枚举/枚举定义", 0)]
    public class ConfigEnumDefinition : ScriptableObject
    {
        [Tooltip("显示名，支持 分类/名称，例如 装备/部位")]
        public string displayName = "分类/名称";

        [Tooltip("生成的 C# 枚举名，例如 EquipSlot")]
        public string className = "NewEnum";

        [Tooltip("false=普通枚举(1,2,3,4...)；true=复合枚举(1,2,4,8...)，生成时加 [Flags]")]
        public bool isFlags;

        [Tooltip("导出目标：客户端 / 服务器 / 两者")]
        public ConfigExportTarget exportTarget = ConfigExportTarget.Both;

        [Tooltip("枚举值列表，用户只填名称和描述，数值自动分配")]
        public List<ConfigEnumValue> values = new List<ConfigEnumValue>();

        /// <summary>计算第 index 个枚举值的数值：普通 = index+1，复合 = 1&lt;&lt;index。</summary>
        public static long ValueOf(ConfigEnumDefinition def, int index)
        {
            return def != null && def.isFlags ? 1L << index : index + 1L;
        }
    }

    /// <summary>单个枚举值（名称 + 描述）。</summary>
    [Serializable]
    public class ConfigEnumValue
    {
        public string name = "Value";
        public string description = "";
    }
}

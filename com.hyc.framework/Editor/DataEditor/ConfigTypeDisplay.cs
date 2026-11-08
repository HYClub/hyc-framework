using System;
using System.Linq;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Shared display-name helpers for config types.
    /// </summary>
    public static class ConfigTypeDisplay
    {
        /// <summary>Last path segment of the [CfgAsset] display name, e.g. "装备" from "示例/装备".</summary>
        public static string GetShortName(Type type)
        {
            if (type == null)
                return "";
            // 只读本体直接标注：配置类由生成器产出，本体必定有一条 [CfgAsset]
            var attr = type.GetCustomAttributes(typeof(CfgAssetAttribute), false).FirstOrDefault() as CfgAssetAttribute;
            if (attr != null && !string.IsNullOrEmpty(attr.Name))
            {
                var parts = attr.Name.Split('/', '\\');
                return parts[parts.Length - 1];
            }
            return type.Name;
        }

        /// <summary>"短名 (类名)" e.g. "装备 (SampleEquipmentConfig)".</summary>
        public static string GetFullLabel(Type type)
            => type == null ? "" : $"{GetShortName(type)} ({type.Name})";
    }
}


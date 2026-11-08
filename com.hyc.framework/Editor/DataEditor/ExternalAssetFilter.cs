using System.IO;
using UnityEditor;
using UnityEngine;

using HYC.Framework.Config;        // ConfigBase / ConfigTemplate / ConfigEnumDefinition
using HYC.Framework.Editor;        // InputComponentMapping / HYCSettings
using HYC.Framework.BT.Editor;     // BTTreeAsset

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 纳入「数据配置」编辑器的外部 Unity 资产过滤器。
    /// <para>一条资产要能被纳入，必须：是主资产（非子资产）、扩展名不在黑名单、主类型不在类型黑名单。</para>
    /// <para><b>扩展名黑名单</b>：代码与源文件（.cs/.js/.ts）、着色器（.shader/.compute/.cginc/.hlsl）、
    /// UI 资源（.uss/.uxml）、程序集定义（.asmdef/.asmref）、预制体（.prefab）。</para>
    /// <para><b>类型黑名单</b>（框架自身产出的资产，不进统一编辑器）：ConfigBase 系、ConfigTemplate、
    /// ConfigEnumDefinition、BTTreeAsset、InputComponentMapping、HYCSettings。</para>
    /// </summary>
    public static class ExternalAssetFilter
    {
        private static readonly string[] BlacklistedExtensions =
        {
            ".cs", ".js", ".ts",
            ".shader", ".compute", ".cginc", ".hlsl",
            ".uss", ".uxml",
            ".asmdef", ".asmref",
            ".prefab",
        };

        /// <summary>扩展名是否黑名单（不区分大小写；无扩展名视为可纳入）。</summary>
        public static bool IsBlacklistedExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;
            var ext = Path.GetExtension(path);
            if (ext.Length == 0)
                return false;
            ext = ext.ToLowerInvariant();
            foreach (var b in BlacklistedExtensions)
                if (ext == b)
                    return true;
            return false;
        }

        /// <summary>主资产类型是否黑名单（含继承 <see cref="ConfigBase"/> 的子类）。</summary>
        public static bool IsBlacklistedType(System.Type type)
        {
            if (type == null)
                return true;
            if (type == typeof(ConfigBase)) return true;
            if (type == typeof(ConfigTemplate)) return true;
            if (type == typeof(ConfigEnumDefinition)) return true;
            if (type == typeof(BTTreeAsset)) return true;
            if (type == typeof(InputComponentMapping)) return true;
            if (type == typeof(HYCSettings)) return true;
            // ConfigBase 子类（ConfigBaseServer / ConfigBaseClient 等）一律排除
            if (typeof(ConfigBase).IsAssignableFrom(type)) return true;
            return false;
        }

        /// <summary>按路径判断（拖拽 / 菜单前期校验用，只看扩展名）。</summary>
        public static bool IsIncludablePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            return !IsBlacklistedExtension(path);
        }
    }
}


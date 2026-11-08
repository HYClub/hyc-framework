using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 类型定义 → **已编译的生成组件**的公共解析器。
    /// <para>
    /// 类型树行的三件事（拖到 GO / 行内 ＋ / 右键加组件）都需要同一个判断：
    /// 「这个定义生成的 <c>MonoBehaviour</c> 现在能不能真的挂上去」。
    /// 生成代码在宿主工程里、包内编译不到，且 Unity 编译完成前 <see cref="MonoScript.GetClass"/> 返回 null，
    /// 所以统一走这里，不在三处各写一份。
    /// </para>
    /// <para>
    /// 注意解析的是 <b>MonoScript</b>，不是 <c>SceneContentTypeDef</c> 资产本身：
    /// 拖出去的必须是脚本资产，Unity 内建才会把它当「加组件」处理。
    /// </para>
    /// </summary>
    public static class SceneContentComponentResolver
    {
        /// <summary>取该定义生成脚本的 <c>MonoScript</c>；文件不存在 / 未导入时返回 null。</summary>
        public static MonoScript ResolveScript(SceneContentTypeDef def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.className))
                return null;

            var path = SceneContentTypeCodeGen.FilePathFor(def.className);
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), path)))
                return null;

            var importer = AssetImporter.GetAtPath(path) as MonoImporter;
            return importer != null ? importer.GetScript() : null;
        }

        /// <summary>取该定义已编译的组件类型；尚未生成、尚未编译或编译失败时返回 null。</summary>
        public static Type Resolve(SceneContentTypeDef def)
        {
            var script = ResolveScript(def);
            if (script == null)
                return null;
            var type = script.GetClass();
            return type != null && typeof(MonoBehaviour).IsAssignableFrom(type) ? type : null;
        }

        /// <summary>不可用时给出可直接展示的中文原因；可用返回 null。</summary>
        public static string FailureHint(SceneContentTypeDef def)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.className))
                return "类型定义缺少类名";
            if (ResolveScript(def) == null)
                return "还没生成代码，请先点「生成代码」";
            if (Resolve(def) == null)
                return "生成脚本还没编译完成，等 Unity 编译结束后再试";
            return null;
        }

        /// <summary>生成脚本在项目内的路径（用于定位 / 拖拽 paths）。</summary>
        public static string ScriptPath(SceneContentTypeDef def)
            => def == null || string.IsNullOrWhiteSpace(def.className)
                ? null
                : SceneContentTypeCodeGen.FilePathFor(def.className);
    }
}

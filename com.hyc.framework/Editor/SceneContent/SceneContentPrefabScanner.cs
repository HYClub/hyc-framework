using System;
using System.Collections.Generic;
using System.Reflection;
using HYC.Framework.Config.Editor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>关卡 prefab 上一个标记组件的扫描结果。</summary>
    public sealed class SceneContentMarkerInfo
    {
        /// <summary>组件本身（编辑器里可 Ping / 选中）。</summary>
        public Component Component;

        /// <summary>组件类型名。</summary>
        public string TypeName;

        /// <summary>组件声明的内容类型 id。</summary>
        public int ContentTypeId;

        /// <summary>该组件是否就属于当前正在编辑的类型定义。</summary>
        public bool MatchesTarget;

        /// <summary>组件在关卡里的层级路径（相对扫描根）。</summary>
        public string Path;
    }

    /// <summary>
    /// 关卡 prefab 的标记组件扫描 + <b>反向填充字段</b>。
    /// <para>
    /// 用途：策划把关卡 prefab 拖到 <see cref="SceneContentTypeDef.scanPrefab"/> 上，
    /// 面板就能列出「这个关卡上挂了哪些场景内容、各是什么类型、几个」，
    /// 并能从一个已有组件的公开字段<b>反推</b>出字段定义列表——省掉手工一个个加字段。
    /// </para>
    /// <para>纯编辑器只读逻辑，不写资产；写资产由调用方（类型定义面板）决定。</para>
    /// </summary>
    public static class SceneContentPrefabScanner
    {
        /// <summary>扫描 <paramref name="root"/> 及其子物体上的全部标记组件。</summary>
        /// <param name="root">扫描根（关卡 prefab 或场景里的关卡根）。</param>
        /// <param name="targetContentTypeId">当前编辑的类型 id，用于标记 <see cref="SceneContentMarkerInfo.MatchesTarget"/>。</param>
        public static List<SceneContentMarkerInfo> Scan(GameObject root, int targetContentTypeId)
        {
            var result = new List<SceneContentMarkerInfo>();
            if (root == null)
                return result;

            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (!(c is ISceneContentMarker marker)) continue;
                result.Add(new SceneContentMarkerInfo
                {
                    Component = c,
                    TypeName = c.GetType().Name,
                    ContentTypeId = marker.ContentTypeId,
                    MatchesTarget = marker.ContentTypeId == targetContentTypeId,
                    Path = SceneContentRefPath.GetPath(root, c.transform),
                });
            }
            return result;
        }

        /// <summary>
        /// 从一个组件的公开序列化字段反推出 <see cref="SceneContentFieldDef"/> 列表。
        /// 无法映射的字段（不支持的类型、非本工程的枚举）会写进 <paramref name="warnings"/>，不静默丢弃。
        /// </summary>
        public static List<SceneContentFieldDef> BuildFieldDefs(Component component, out List<string> warnings)
        {
            warnings = new List<string>();
            var defs = new List<SceneContentFieldDef>();
            if (component == null)
                return defs;

            var fields = component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var fi in fields)
            {
                var def = MapField(fi, warnings);
                if (def != null)
                    defs.Add(def);
            }
            return defs;
        }

        private static SceneContentFieldDef MapField(FieldInfo fi, List<string> warnings)
        {
            var t = fi.FieldType;
            var isList = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
            if (isList)
                t = t.GetGenericArguments()[0];

            var def = new SceneContentFieldDef
            {
                name = fi.Name,
                isList = isList,
                clientExport = new SceneContentFieldExport(SceneContentExportMode.Direct),
                serverExport = new SceneContentFieldExport(SceneContentExportMode.None),
            };

            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte))
            {
                def.type = SceneContentFieldType.Int;
                return def;
            }
            if (t == typeof(float) || t == typeof(double))
            {
                def.type = SceneContentFieldType.Float;
                return def;
            }
            if (t == typeof(bool))
            {
                def.type = SceneContentFieldType.Bool;
                return def;
            }
            if (t == typeof(string))
            {
                def.type = SceneContentFieldType.String;
                return def;
            }
            if (t == typeof(GameObject))
            {
                def.type = SceneContentFieldType.GameObject;
                // GO 引用默认导出成层级路径：这是唯一跨会话稳定的表示（见 SceneContentRefPath）
                def.clientExport = new SceneContentFieldExport(SceneContentExportMode.HierarchyPath);
                return def;
            }
            if (t.IsEnum)
            {
                def.type = SceneContentFieldType.Enum;
                def.enumRefClassName = t.Name;
                if (ConfigEnumCodeGen.FindEnum(t.Name) == null)
                    warnings.Add($"字段「{fi.Name}」的枚举 {t.Name} 在数据编辑器里没有对应枚举定义，生成前需先建一个同名枚举");
                return def;
            }

            warnings.Add($"字段「{fi.Name}」类型 {t.Name} 暂不支持，已跳过");
            return null;
        }
    }
}

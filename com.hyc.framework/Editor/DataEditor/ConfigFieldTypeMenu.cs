using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 字段类型下拉的<b>公共菜单</b>：与配置模板编辑器同一套分类路径与动态引用项。
    /// <para>
    /// 值域是 <see cref="ConfigFieldType"/>（配置模板那份最全的类型清单）。调用方只支持其中一部分时，
    /// 把支持集合传给 <see cref="BuildMenu"/>——不支持的项<b>仍留在菜单里但置灰</b>，不删、不另造一套。
    /// 选中后写哪些字段、导出设置怎么复位，由调用方在回调里自己决定，本类不碰 SerializedProperty。
    /// </para>
    /// </summary>
    public static class ConfigFieldTypeMenu
    {
        /// <summary>字段类型 → 菜单分类路径（值类型 / Unity 数学 / Unity 对象 / 框架 / 多语言）。</summary>
        public static readonly Dictionary<ConfigFieldType, string> TypeMenuPaths = new Dictionary<ConfigFieldType, string>
        {
            // 值类型
            { ConfigFieldType.String, "值类型/字符串" },
            { ConfigFieldType.Int, "值类型/整数" },
            { ConfigFieldType.Long, "值类型/整数" },
            { ConfigFieldType.Short, "值类型/整数" },
            { ConfigFieldType.Byte, "值类型/整数" },
            { ConfigFieldType.UInt, "值类型/整数" },
            { ConfigFieldType.Float, "值类型/浮点" },
            { ConfigFieldType.Double, "值类型/浮点" },
            { ConfigFieldType.Decimal, "值类型/浮点" },
            { ConfigFieldType.Bool, "值类型/布尔" },
            { ConfigFieldType.Char, "值类型/字符" },
            // Unity 数学类型
            { ConfigFieldType.Vector2, "Unity 数学/向量" },
            { ConfigFieldType.Vector3, "Unity 数学/向量" },
            { ConfigFieldType.Vector4, "Unity 数学/向量" },
            { ConfigFieldType.Vector2Int, "Unity 数学/向量" },
            { ConfigFieldType.Vector3Int, "Unity 数学/向量" },
            { ConfigFieldType.Quaternion, "Unity 数学/旋转" },
            { ConfigFieldType.Color, "Unity 数学/颜色" },
            { ConfigFieldType.Color32, "Unity 数学/颜色" },
            { ConfigFieldType.Rect, "Unity 数学/矩形" },
            { ConfigFieldType.RectInt, "Unity 数学/矩形" },
            { ConfigFieldType.RectOffset, "Unity 数学/矩形" },
            { ConfigFieldType.Bounds, "Unity 数学/边界" },
            { ConfigFieldType.BoundsInt, "Unity 数学/边界" },
            { ConfigFieldType.Gradient, "Unity 数学/曲线" },
            { ConfigFieldType.AnimationCurve, "Unity 数学/曲线" },
            { ConfigFieldType.LayerMask, "Unity 数学/其他" },
            // Unity 对象类型
            { ConfigFieldType.Texture2D, "Unity 对象/贴图" },
            { ConfigFieldType.Sprite, "Unity 对象/贴图" },
            { ConfigFieldType.Material, "Unity 对象/材质" },
            { ConfigFieldType.Shader, "Unity 对象/材质" },
            { ConfigFieldType.Mesh, "Unity 对象/网格" },
            { ConfigFieldType.PhysicMaterial, "Unity 对象/物理" },
            { ConfigFieldType.GameObject, "Unity 对象/游戏对象" },
            { ConfigFieldType.AudioClip, "Unity 对象/音频" },
            { ConfigFieldType.Font, "Unity 对象/文本" },
            { ConfigFieldType.TextAsset, "Unity 对象/文本" },
            { ConfigFieldType.Object, "Unity 对象/通用" },
            // 框架能力
            { ConfigFieldType.BehaviourTree, "框架/行为树" },
            { ConfigFieldType.AnimationClip, "框架/动画片段" },
            { ConfigFieldType.AnimatorController, "框架/动画控制器" },
            { ConfigFieldType.Addressable, "框架/资源地址" },
            { ConfigFieldType.AudioEvent, "框架/音频" },
            // 多语言
            { ConfigFieldType.LocalizedKey, "多语言/多语言Key" },
        };

        /// <summary>一次菜单选择的结果（<c>Enum</c> 带枚举类名，<c>Reference</c> 带引用类型全名）。</summary>
        public struct Selection
        {
            public ConfigFieldType type;
            public string enumRefClassName;
            public string refTypeFullName;
        }

        /// <summary>一个枚举定义菜单项。</summary>
        public struct EnumRefItem
        {
            public string display;
            public string className;
        }

        /// <summary>一个配置引用菜单项。</summary>
        public struct CfgRefItem
        {
            public string display;
            public Type type;
        }

        /// <summary>工程内全部枚举定义（<c>ConfigEnumDefinition</c>），供「引用/枚举引用」段使用。</summary>
        public static List<EnumRefItem> EnumRefItems()
        {
            var result = new List<EnumRefItem>();
            foreach (var def in ConfigEnumCodeGen.LoadAllEnums())
            {
                if (def == null)
                    continue;
                result.Add(new EnumRefItem
                {
                    display = string.IsNullOrEmpty(def.displayName) ? def.className : def.displayName,
                    className = def.className,
                });
            }
            return result;
        }

        /// <summary>工程内全部可引用的配置类型（带 [CfgAsset] 且非隐藏），按名字排序。</summary>
        public static List<CfgRefItem> CfgRefItems()
        {
            return TypeCache.GetTypesWithAttribute<CfgAssetAttribute>()
                .Where(t => typeof(ScriptableObject).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericType
                            && !CfgAssetAttribute.IsHidden(t))
                .OrderBy(t => t.Name)
                .Select(t => new CfgRefItem { display = ConfigTypeDisplay.GetShortName(t), type = t })
                .ToList();
        }

        /// <summary>该类型是否依赖 loc 多语言包（仅 <see cref="ConfigFieldType.LocalizedKey"/>）。</summary>
        public static bool NeedsLocPackage(ConfigFieldType type)
            => type == ConfigFieldType.LocalizedKey;

        /// <summary>下拉按钮上显示的标签（引用 / 枚举 / 框架能力项有专门文案）。</summary>
        public static string GetLabel(ConfigFieldType type, string enumRefClassName, string refTypeFullName)
        {
            switch (type)
            {
                case ConfigFieldType.Reference:
                {
                    if (ConfigTemplateCodeGen.IsSelfRefKeyword(refTypeFullName))
                        return "引用:self（未解析）";
                    var refType = ConfigTemplateCodeGen.ResolveRefTypeForUi(refTypeFullName);
                    if (refType != null)
                        return ConfigTypeDisplay.GetFullLabel(refType);
                    // 模板已建、代码未生成时（含自引用字段）按模板 className 显示，避免误报「未选择」
                    var pendingClass = ConfigTemplateCodeGen.GeneratedTemplateRefShortName(refTypeFullName);
                    return pendingClass != null ? $"引用:{pendingClass}（代码未生成）" : "引用:未选择";
                }
                case ConfigFieldType.Enum:
                {
                    var e = ConfigEnumCodeGen.FindEnum(enumRefClassName);
                    return e != null ? $"枚举:{e.className}" : "枚举:未选择";
                }
                case ConfigFieldType.LocalizedKey: return "多语言Key";
                case ConfigFieldType.BehaviourTree: return "行为树";
                case ConfigFieldType.AnimationClip: return "动画片段";
                case ConfigFieldType.AnimatorController: return "动画控制器";
                case ConfigFieldType.Addressable: return "资源地址";
                case ConfigFieldType.AudioEvent: return "音频";
                default: return type.ToString();
            }
        }

        /// <summary>下拉按钮上显示的标签。</summary>
        public static string GetLabel(Selection current)
            => GetLabel(current.type, current.enumRefClassName, current.refTypeFullName);

        /// <summary>
        /// 构建完整的类型菜单：分类基础类型 + 配置引用 + 枚举引用。
        /// <para>
        /// <paramref name="unsupportedReason"/> 返回 null 表示该调用方支持此类型；返回非 null 时该项
        /// 用 <c>AddDisabledItem</c> <b>留在菜单里置灰</b>，文案追加到标签末尾说明原因。传 null = 全部可用。
        /// 语言包未安装时多语言项额外置灰，与配置模板编辑器一致。
        /// </para>
        /// </summary>
        public static GenericMenu BuildMenu(Selection current,
            Func<ConfigFieldType, string> unsupportedReason, Action<Selection> onSelect)
        {
            var menu = new GenericMenu();

            foreach (var kv in TypeMenuPaths)
            {
                var item = kv.Key;
                var path = $"{kv.Value}/{item}";
                var reason = Reason(item, unsupportedReason);
                if (reason != null)
                {
                    menu.AddDisabledItem(new GUIContent(path + reason));
                    continue;
                }
                menu.AddItem(new GUIContent(path), current.type == item, () =>
                    onSelect?.Invoke(new Selection { type = item }));
            }

            menu.AddSeparator("");

            var cfgTypes = CfgRefItems();
            var refReason = Reason(ConfigFieldType.Reference, unsupportedReason);
            foreach (var c in cfgTypes)
            {
                var cfg = c;
                var path = $"引用/配置引用/{cfg.display} ({cfg.type.Name})";
                if (refReason != null)
                {
                    menu.AddDisabledItem(new GUIContent(path + refReason));
                    continue;
                }
                menu.AddItem(new GUIContent(path),
                    current.type == ConfigFieldType.Reference
                    && current.refTypeFullName == cfg.type.AssemblyQualifiedName,
                    () => onSelect?.Invoke(new Selection
                    {
                        type = ConfigFieldType.Reference,
                        refTypeFullName = cfg.type.AssemblyQualifiedName,
                    }));
            }
            if (cfgTypes.Count == 0)
                menu.AddDisabledItem(new GUIContent("引用/配置引用/（暂无配置类型）"));

            var enums = EnumRefItems();
            var enumReason = Reason(ConfigFieldType.Enum, unsupportedReason);
            foreach (var e in enums)
            {
                var en = e;
                var path = $"引用/枚举引用/{en.display} ({en.className})";
                if (enumReason != null)
                {
                    menu.AddDisabledItem(new GUIContent(path + enumReason));
                    continue;
                }
                menu.AddItem(new GUIContent(path),
                    current.type == ConfigFieldType.Enum && current.enumRefClassName == en.className,
                    () => onSelect?.Invoke(new Selection
                    {
                        type = ConfigFieldType.Enum,
                        enumRefClassName = en.className,
                    }));
            }
            if (enums.Count == 0)
                menu.AddDisabledItem(new GUIContent("引用/枚举引用/（暂无枚举）"));

            return menu;
        }

        /// <summary>null = 可用；非 null = 置灰原因（追加到菜单项标签末尾）。</summary>
        private static string Reason(ConfigFieldType type, Func<ConfigFieldType, string> unsupportedReason)
        {
            if (NeedsLocPackage(type) && !LocAccess.IsLocInstalled)
                return "（需本地化包）";
            return unsupportedReason?.Invoke(type);
        }
    }
}

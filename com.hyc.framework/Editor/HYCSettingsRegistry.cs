using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// HYC Settings 窗口的**配置式分组注册表**。
    ///
    /// <para>扫描所有「引用了本程序集」的程序集，收集带
    /// <see cref="HYCSettingsGroupAttribute"/> 的静态字段/方法，以及已登记宿主
    /// （<see cref="RegisterHost"/>，窗口登记自身）的实例字段/方法，
    /// 按特性里的路径拼成一棵分组树，供窗口左栏渲染、右栏按选中节点出内容。</para>
    ///
    /// <para><b>为什么用反射而不是手写列表</b>：手写的分组清单每加一项都要改窗口文件，
    /// 使用方工程想加设置就得改框架；反射扫描让「加一项 = 加一行特性」，
    /// 结果与顺序也不再依赖某个人记得同步两处。</para>
    ///
    /// <para>结果按域缓存，<see cref="Rebuild"/> 可强制重建（宿主增减、域重载后）。</para>
    /// </summary>
    public static class HYCSettingsRegistry
    {
        /// <summary>一个分组节点：可同时挂本组条目与子分组。</summary>
        public sealed class Node
        {
            /// <summary>分组名（路径末段）。根节点为空串。</summary>
            public string Name = "";

            /// <summary>完整路径（父路径用 / 连接），同时是选中态的唯一标识。</summary>
            public string Path = "";

            /// <summary>挂在本节点的条目（已排序）。</summary>
            public readonly List<Item> Items = new List<Item>();

            /// <summary>子分组（已按名称排序）。</summary>
            public readonly List<Node> Children = new List<Node>();
        }

        /// <summary>一个设置条目：一个自动绘制的字段，或一个自定义绘制方法。</summary>
        public sealed class Item
        {
            /// <summary>显示名（字段标签；方法仅作备注）。</summary>
            public string Label = "";

            /// <summary>组内排序。</summary>
            public int Order;

            /// <summary>绘制回调。</summary>
            public Action Draw;

            /// <summary>字段条目才有：字段信息。方法条目为 null。</summary>
            public FieldInfo Field;

            /// <summary>字段所属宿主（静态字段为 null）。</summary>
            public object Host;

            /// <summary>字段条目才有：特性实例（取持久化键与开关）。</summary>
            public HYCSettingsGroupAttribute Attribute;
        }

        private const string PrefixKeyPrefix = "HYC.Framework.Settings.";

        private const BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private const BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static readonly List<object> Hosts = new List<object>();
        private static readonly Type[] NoTypes = new Type[0];
        private static Node _root;

        /// <summary>分组树根节点。首次访问时构建并缓存。</summary>
        public static Node Root
        {
            get
            {
                if (_root == null)
                    Rebuild();
                return _root;
            }
        }

        /// <summary>
        /// 登记一个宿主对象：它的实例字段/方法会被纳入扫描，并立刻回填已持久化的字段值。
        /// 重复登记同一实例不会重复添加。
        /// </summary>
        public static void RegisterHost(object host)
        {
            if (host == null)
                return;
            if (!Hosts.Contains(host))
                Hosts.Add(host);
            Rebuild();
        }

        /// <summary>注销宿主（窗口关闭时），并重建分组树。</summary>
        public static void UnregisterHost(object host)
        {
            if (host == null)
                return;
            if (Hosts.Remove(host))
                Rebuild();
        }

        /// <summary>强制重建分组树（新加/改了特性而域未重载时可用）。</summary>
        public static void Rebuild()
        {
            _root = Build();
        }

        /// <summary>按路径找节点；找不到返回 null。</summary>
        public static Node Find(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            return FindRecursive(Root, path);
        }

        /// <summary>取第一个可见节点（左栏默认选中项）。没有分组时返回 null。</summary>
        public static Node FirstNode()
        {
            var root = Root;
            return root.Children.Count > 0 ? root.Children[0] : null;
        }

        // ---------------------------------------------------------------- 构建

        private static Node Build()
        {
            var root = new Node();
            foreach (var asm in CollectAssemblies())
            {
                var types = SafeGetTypes(asm);
                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsGenericTypeDefinition)
                        continue;

                    CollectMembers(type, null, StaticFlags, root);

                    // 实例成员：只扫登记过的宿主（避免随手 new 出编辑器对象）
                    for (var i = 0; i < Hosts.Count; i++)
                    {
                        var host = Hosts[i];
                        if (host != null && host.GetType() == type)
                            CollectMembers(type, host, InstanceFlags, root);
                    }
                }
            }

            SortRecursive(root);
            LoadPersisted(root);
            return root;
        }

        private static void CollectMembers(Type type, object host, BindingFlags flags, Node root)
        {
            var methods = type.GetMethods(flags);
            for (var i = 0; i < methods.Length; i++)
                CollectMethod(methods[i], host, root);

            var fields = type.GetFields(flags);
            for (var i = 0; i < fields.Length; i++)
                CollectField(fields[i], host, root);
        }

        private static void CollectMethod(MethodInfo method, object host, Node root)
        {
            var att = method.GetCustomAttribute<HYCSettingsGroupAttribute>();
            if (att == null)
                return;
            if (method.IsGenericMethodDefinition || method.GetParameters().Length != 0 ||
                method.ReturnType != typeof(void))
            {
                Debug.LogWarning($"[HYC Settings] 方法 {method.DeclaringType?.Name}.{method.Name} 带 " +
                                 $"{nameof(HYCSettingsGroupAttribute)}，但签名必须是无参 void，已忽略。");
                return;
            }

            var node = GetOrCreateNode(root, att.Path);
            if (node == null)
                return;

            var capturedHost = host;
            var capturedMethod = method;
            node.Items.Add(new Item
            {
                Label = string.IsNullOrEmpty(att.Label) ? Nicify(method.Name) : att.Label,
                Order = att.Order,
                Draw = () =>
                {
                    try
                    {
                        capturedMethod.Invoke(capturedHost, null);
                    }
                    catch (TargetInvocationException ex)
                    {
                        // 不让某一个分组的异常把整个窗口画崩
                        Debug.LogException(ex.InnerException ?? ex);
                    }
                }
            });
        }

        private static void CollectField(FieldInfo field, object host, Node root)
        {
            var att = field.GetCustomAttribute<HYCSettingsGroupAttribute>();
            if (att == null)
                return;

            var node = GetOrCreateNode(root, att.Path);
            if (node == null)
                return;

            node.Items.Add(new Item
            {
                Label = string.IsNullOrEmpty(att.Label) ? Nicify(field.Name) : att.Label,
                Order = att.Order,
                Field = field,
                Host = host,
                Attribute = att,
                Draw = () => DrawFieldItem(field, host, att)
            });
        }

        private static Node GetOrCreateNode(Node root, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"[HYC Settings] {nameof(HYCSettingsGroupAttribute)} 的路径为空，已忽略。");
                return null;
            }

            var parts = path.Split('/');
            var cur = root;
            var acc = "";
            foreach (var raw in parts)
            {
                var seg = raw.Trim();
                if (seg.Length == 0)
                    continue;
                acc = acc.Length == 0 ? seg : acc + "/" + seg;

                Node child = null;
                for (var i = 0; i < cur.Children.Count; i++)
                {
                    if (cur.Children[i].Name == seg)
                    {
                        child = cur.Children[i];
                        break;
                    }
                }
                if (child == null)
                {
                    child = new Node { Name = seg, Path = acc };
                    cur.Children.Add(child);
                }
                cur = child;
            }

            // 路径全是分隔符 → 退化成根节点，不挂载（根节点没有可显示的名字）
            return cur == root ? null : cur;
        }

        private static void SortRecursive(Node node)
        {
            node.Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            node.Items.Sort((a, b) =>
            {
                if (a.Order != b.Order)
                    return a.Order.CompareTo(b.Order);
                return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var child in node.Children)
                SortRecursive(child);
        }

        private static Node FindRecursive(Node node, string path)
        {
            if (node.Path == path)
                return node;
            foreach (var child in node.Children)
            {
                var hit = FindRecursive(child, path);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        // ---------------------------------------------------------------- 字段绘制与持久化

        private static void DrawFieldItem(FieldInfo field, object host, HYCSettingsGroupAttribute att)
        {
            var t = field.FieldType;
            if (!IsSupported(t))
            {
                EditorGUILayout.HelpBox(
                    $"设置字段 {field.Name} 的类型 {t.Name} 不受支持（支持 bool / int / float / string / 枚举）。" +
                    "需要复杂控件时，请改用带特性的无参 void 方法自行绘制。",
                    MessageType.Warning);
                return;
            }

            var label = string.IsNullOrEmpty(att.Label) ? Nicify(field.Name) : att.Label;
            var cur = field.GetValue(host);

            EditorGUI.BeginChangeCheck();
            object next;
            if (t == typeof(bool))
                next = EditorGUILayout.Toggle(label, (bool)cur);
            else if (t == typeof(int))
                next = EditorGUILayout.IntField(label, (int)cur);
            else if (t == typeof(float))
                next = EditorGUILayout.FloatField(label, (float)cur);
            else if (t == typeof(string))
                next = EditorGUILayout.TextField(label, (string)cur);
            else
                next = EditorGUILayout.EnumPopup(label, (Enum)cur);

            if (!EditorGUI.EndChangeCheck())
                return;

            field.SetValue(host, next);
            if (att.Persist)
                WriteEntry(PrefKey(field, att), next);
        }

        private static void LoadPersisted(Node node)
        {
            foreach (var item in node.Items)
            {
                if (item.Field != null)
                    LoadField(item);
            }
            foreach (var child in node.Children)
                LoadPersisted(child);
        }

        private static void LoadField(Item item)
        {
            var att = item.Attribute;
            if (att == null || !att.Persist)
                return;

            var t = item.Field.FieldType;
            if (!IsSupported(t))
                return;

            var key = PrefKey(item.Field, att);
            var settings = HYCSettings.Instance;
            if (settings == null || !settings.HasEntry(key))
                return; // 没存过 → 保留代码里的默认值

            var raw = settings.GetEntry(key);
            object value;
            if (t == typeof(bool))
            {
                bool.TryParse(raw, out var b);
                value = b;
            }
            else if (t == typeof(int))
            {
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i);
                value = i;
            }
            else if (t == typeof(float))
            {
                float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f);
                value = f;
            }
            else if (t == typeof(string))
            {
                value = raw;
            }
            else
            {
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e);
                value = Enum.ToObject(t, e);
            }

            item.Field.SetValue(item.Host, value);
        }

        private static string PrefKey(FieldInfo field, HYCSettingsGroupAttribute att)
        {
            if (!string.IsNullOrEmpty(att.Key))
                return att.Key;
            var owner = field.DeclaringType != null ? field.DeclaringType.FullName : "Unknown";
            return PrefixKeyPrefix + owner + "." + field.Name;
        }

        private static void WriteEntry(string key, object value)
        {
            HYCSettings.Instance.SetEntry(key, ToEntryString(value));
        }

        /// <summary>
        /// 把字段值转成条目字符串（与 <see cref="LoadField"/> 的解析严格配对）。
        /// 浮点用不变区域性往返，避免逗号小数点文化把设置写坏。
        /// </summary>
        private static string ToEntryString(object value)
        {
            switch (value)
            {
                case bool b: return b.ToString();
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case float f: return f.ToString(CultureInfo.InvariantCulture);
                case string s: return s;
                case Enum e: return Convert.ToInt32(e).ToString(CultureInfo.InvariantCulture);
                default: return value != null ? value.ToString() : string.Empty;
            }
        }

        private static bool IsSupported(Type t)
        {
            return t == typeof(bool) || t == typeof(int) || t == typeof(float) ||
                   t == typeof(string) || t.IsEnum;
        }

        // ---------------------------------------------------------------- 反射辅助

        /// <summary>
        /// 只扫「引用了本程序集」的程序集：既能让使用方工程扩展设置，
        /// 又不会把 Unity 自带与第三方的一大堆程序集挨个 GetTypes（那才是真正的开销）。
        /// </summary>
        private static IEnumerable<Assembly> CollectAssemblies()
        {
            var self = typeof(HYCSettingsRegistry).Assembly;
            yield return self;

            var selfName = self.GetName().Name;
            var all = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in all)
            {
                if (asm == self)
                    continue;

                AssemblyName[] refs;
                try
                {
                    refs = asm.GetReferencedAssemblies();
                }
                catch (Exception)
                {
                    continue; // 动态程序集可能取不到引用表
                }

                foreach (var r in refs)
                {
                    if (r.Name == selfName)
                    {
                        yield return asm;
                        break;
                    }
                }
            }
        }

        private static Type[] SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types ?? NoTypes;
            }
            catch (Exception)
            {
                return NoTypes;
            }
        }

        /// <summary>
        /// 成员名易读化：<c>_autoGen</c> → <c>Auto Gen</c>，<c>DrawIdServerSection</c> → <c>Draw Id Server Section</c>。
        /// 刻意不依赖 Unity 的 <c>ObjectNames</c>：本文件在编辑器下跑，但保持零额外依赖更省心。
        /// </summary>
        private static string Nicify(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var s = name.TrimStart('_');
            if (s.Length == 0)
                return name;

            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]))
                    sb.Append(' ');
                if (i > 1 && char.IsUpper(c) && char.IsUpper(s[i - 1]) && i + 1 < s.Length && char.IsLower(s[i + 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

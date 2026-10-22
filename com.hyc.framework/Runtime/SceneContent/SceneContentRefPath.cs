using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 关卡内 GameObject 的<b>稳定引用</b>：层级路径的生成与反解。
    /// <para>
    /// 为什么需要它：<c>JsonUtility.ToJson</c> 把 <c>GameObject</c> 字段写成
    /// <c>{"instanceID":-1234}</c>——instanceID 换会话/换机器就变，不能落进 blob。
    /// 导出时必须转成「相对关卡根的层级路径」这种可复现的字符串。
    /// </para>
    /// <para>
    /// 路径形如 <c>Gate/Door#1</c>：段间用 <c>/</c>，<b>仅在同级重名时</b>追加 <c>#序号</c>
    /// （序号 = 在父节点 children 里的下标），重名消失后序号自动去掉。
    /// GameObject 名里不要含 <c>/</c>（Unity 层级本身也不支持），否则路径不可反解。
    /// </para>
    /// </summary>
    public static class SceneContentRefPath
    {
        private const char Separator = '/';
        private const char IndexMark = '#';

        /// <summary>
        /// 资源路径解析器（由 Editor 程序集在加载时注入，见 <c>SceneContentRefProviders</c>）。
        /// 运行期为空：payload 在编辑器里产出，运行期不解析资源路径。
        /// </summary>
        public static System.Func<GameObject, string> AssetPathProvider;

        /// <summary>Addressable 地址解析器（同上，Editor 注入）。</summary>
        public static System.Func<GameObject, string> AddressableProvider;

        /// <summary>取 GO 对应资源的项目内路径；无解析器或对象非资产时返回 null。</summary>
        public static string GetAssetPath(GameObject go)
            => go == null || AssetPathProvider == null ? null : AssetPathProvider(go);

        /// <summary>取 GO 对应资源的 Addressable 地址；无解析器或未注册时返回 null。</summary>
        public static string GetAddressableAddress(GameObject go)
            => go == null || AddressableProvider == null ? null : AddressableProvider(go);

        /// <summary>
        /// 生成 <paramref name="target"/> 相对 <paramref name="root"/> 的层级路径。
        /// <para>target 为空返回 null；target 就是 root 返回空串；
        /// target 不在 root 之下（含根本身为空）返回 null——调用方据此写空值。</para>
        /// </summary>
        public static string GetPath(GameObject root, Transform target)
        {
            if (target == null)
                return null;
            if (root == null)
                return null;

            var rootTransform = root.transform;
            if (target == rootTransform)
                return string.Empty;

            var segments = new List<string>();
            var cur = target;
            while (cur != null && cur != rootTransform)
            {
                segments.Add(MakeSegment(cur));
                cur = cur.parent;
            }

            // 一路向上没走到 root ⇒ 不是这棵子树下的对象，路径无意义
            if (cur != rootTransform)
                return null;

            segments.Reverse();
            var sb = new StringBuilder();
            for (var i = 0; i < segments.Count; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(segments[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 按 <see cref="GetPath"/> 产出的路径反解回 Transform。
        /// 路径为空 / 找不到 / 中途断链时返回 null（不抛异常，调用方按缺省处理）。
        /// </summary>
        public static Transform Resolve(GameObject root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
                return null;

            var cur = root.transform;
            var start = 0;
            while (start <= path.Length)
            {
                var end = path.IndexOf(Separator, start);
                if (end < 0) end = path.Length;
                var segment = path.Substring(start, end - start);
                if (segment.Length == 0)
                    return null;

                cur = FindChild(cur, segment);
                if (cur == null)
                    return null;

                if (end >= path.Length)
                    break;
                start = end + 1;
            }
            return cur;
        }

        /// <summary>拼一段路径文本：同级重名时带 <c>#下标</c>。</summary>
        private static string MakeSegment(Transform t)
        {
            var parent = t.parent;
            if (parent == null)
                return t.name;

            var sameName = 0;
            var index = -1;
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!string.Equals(child.name, t.name, System.StringComparison.Ordinal))
                    continue;
                sameName++;
                if (child == t) index = i;
            }

            if (sameName <= 1 || index < 0)
                return t.name;

            var sb = new StringBuilder(t.name);
            sb.Append(IndexMark).Append(index);
            return sb.ToString();
        }

        /// <summary>按段文本找子节点（含重名下标解析）。</summary>
        private static Transform FindChild(Transform parent, string segment)
        {
            var mark = segment.LastIndexOf(IndexMark);
            if (mark >= 0 && mark < segment.Length - 1)
            {
                var namePart = segment.Substring(0, mark);
                if (int.TryParse(segment.Substring(mark + 1), out var index)
                    && index >= 0 && index < parent.childCount)
                {
                    var child = parent.GetChild(index);
                    if (string.Equals(child.name, namePart, System.StringComparison.Ordinal))
                        return child;
                }
                // 下标对不上（层级被改过）→ 退化成按名字找第一个
                return FindFirstByName(parent, namePart);
            }

            return FindFirstByName(parent, segment);
        }

        private static Transform FindFirstByName(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, name, System.StringComparison.Ordinal))
                    return child;
            }
            return null;
        }
    }
}

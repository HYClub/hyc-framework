using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.SceneContent
{
    /// <summary>
    /// 关卡描述器：挂在关卡 prefab 根上的「接入 HYC 的钩子」。
    /// 聚合两类内容来源，合并导出/加载为一份 Blob：
    /// <list type="number">
    ///   <item><b>A. 显式实例条目</b>（<see cref="entries"/> 列表，编辑器拖 prefab + transform + payload）；</item>
    ///   <item><b>B. 标记组件</b>（自身或子物体上实现 <see cref="ISceneContentMarker"/> 的 MonoBehaviour，如出生点）。</item>
    /// </list>
    /// <para>运行期不依赖本组件：游戏在场景加载完成后调 <see cref="SceneContentLoader.Load(int, ISceneContentSpawner)"/>。</para>
    /// </summary>
    public class SceneContentAuthoring : MonoBehaviour
    {
        /// <summary>关卡 id。导出文件名与运行期 Load 的入参必须与之匹配。</summary>
        [Tooltip("关卡 id。导出文件名与运行期 SceneContentLoader.Load 的入参必须与之匹配。")]
        public int levelId;

        /// <summary>是否同时导出服务端产物（无服务端的纯单机工程可关掉）。</summary>
        [Tooltip("是否同时导出服务端产物（StreamingAssets/SceneContent/server/）。纯单机工程可关掉。")]
        public bool exportServer = true;

        /// <summary>A. 显式实例条目：编辑器拖 prefab + 摆放 transform + 自定义参数（JSON）。</summary>
        [Tooltip("A. 显式实例条目：编辑器拖 prefab + 摆放 transform + 自定义参数（JSON）。")]
        public List<SceneContentEntry> entries = new List<SceneContentEntry>();

        /// <summary>
        /// B. 自动发现自身及子物体上实现 <see cref="ISceneContentMarker"/> 的组件，转成统一条目数据（客户端侧）。
        /// <see cref="SceneContentEntryData.contentTypeId"/> 取自组件自身。
        /// </summary>
        public List<SceneContentEntryData> GetMarkerEntries() => GetMarkerEntries(SceneContentSide.Client);

        /// <summary>
        /// B. 自动发现自身及子物体上实现 <see cref="ISceneContentMarker"/> 的组件，转成统一条目数据。
        /// <para>
        /// payload 优先走 <see cref="ISceneContentMarkerPayload"/>（可按侧裁剪字段、把 GO 引用转成层级路径）；
        /// 组件没实现该契约时退回 <see cref="JsonUtility.ToJson"/>，此时<b>不能有 GO/Transform 字段</b>
        /// —— 那个 API 会写成 instanceID，跨会话不稳定。
        /// </para>
        /// </summary>
        public List<SceneContentEntryData> GetMarkerEntries(SceneContentSide side)
        {
            var result = new List<SceneContentEntryData>();
            var markers = GetComponentsInChildren<Component>(true);
            foreach (var c in markers)
            {
                if (c == null) continue;
                if (!(c is ISceneContentMarker marker)) continue;
                var t = c.transform;
                var payload = c is ISceneContentMarkerPayload payloadProvider
                    ? payloadProvider.ToPayloadJson(gameObject, side)
                    : JsonUtility.ToJson(c);
                result.Add(new SceneContentEntryData
                {
                    contentTypeId = marker.ContentTypeId,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                    payloadJson = payload,
                });
            }
            return result;
        }
    }

    /// <summary>A. 显式实例条目（编辑器可序列化）。prefab 为直接资源引用（仅编辑器用，不进 Blob）。</summary>
    [System.Serializable]
    public class SceneContentEntry
    {
        public GameObject prefab;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        [TextArea(1, 4)] public string payloadJson;
    }

    /// <summary>统一条目数据载体（编辑器与运行期之间的中性传输结构）。</summary>
    [System.Serializable]
    public class SceneContentEntryData
    {
        public int contentTypeId;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public string payloadJson;
    }
}

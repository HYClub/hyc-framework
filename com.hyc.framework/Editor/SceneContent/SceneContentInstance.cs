using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 编辑器内标记：预览生成的实例、或手工摆放并「标记为场景内容」的 GO 上挂此组件，
    /// 供 <see cref="SceneContentCapturer"/> 读回 <see cref="SceneContentAuthoring"/> 的实例条目列表。
    /// 仅编辑器使用，运行期不依赖。
    /// </summary>
    public class SceneContentInstance : MonoBehaviour
    {
        public GameObject sourcePrefab;
        public int contentTypeId = -1;
        [TextArea(1, 4)] public string payloadJson;
    }
}

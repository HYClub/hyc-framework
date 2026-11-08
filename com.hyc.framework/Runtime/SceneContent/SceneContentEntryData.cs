using System;
using UnityEngine;

namespace HYC.Framework.SceneContent
{
    /// <summary>统一条目数据载体（编辑器与运行期之间的中性传输结构）。</summary>
    [Serializable]
    public class SceneContentEntryData
    {
        public int contentTypeId;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public string payloadJson;
    }
}

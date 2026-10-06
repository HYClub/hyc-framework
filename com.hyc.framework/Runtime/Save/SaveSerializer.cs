using System;
using UnityEngine;

namespace HYC.Framework.Save
{
    /// <summary>
    /// 存档的序列化抽象。<see cref="SaveManager"/> 只认这个接口，
    /// 换序列化器不需要改动存储层。
    /// </summary>
    public interface ISaveSerializer
    {
        /// <summary>把对象转成可写盘的文本。失败时抛异常，由调用方处理。</summary>
        string Serialize(object obj);

        /// <summary>把文本还原成对象。文本非法时抛异常，由调用方处理。</summary>
        T Deserialize<T>(string text);
    }

    /// <summary>
    /// 默认实现：Newtonsoft.Json。支持 <c>Dictionary</c>、多态、顶层 <c>List&lt;T&gt;</c>，
    /// 代价是多一个包依赖（<c>com.unity.nuget.newtonsoft-json</c>）与 IL2CPP 下走反射
    /// （存档读写不频繁，这个开销可以接受）。
    /// </summary>
    public sealed class NewtonsoftSaveSerializer : ISaveSerializer
    {
        private readonly Newtonsoft.Json.JsonSerializerSettings _settings;

        /// <param name="prettyPrint">缩进输出，便于开发期直接打开存档文件看内容；出包建议 false。</param>
        public NewtonsoftSaveSerializer(bool prettyPrint = false)
        {
            _settings = new Newtonsoft.Json.JsonSerializerSettings
            {
                Formatting = prettyPrint
                    ? Newtonsoft.Json.Formatting.Indented
                    : Newtonsoft.Json.Formatting.None,
                // 存什么取什么，不做类型名包裹，避免类型改名后读不回来
                TypeNameHandling = Newtonsoft.Json.TypeNameHandling.None,
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
            };
        }

        public string Serialize(object obj)
            => Newtonsoft.Json.JsonConvert.SerializeObject(obj, _settings);

        public T Deserialize<T>(string text)
            => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(text, _settings);
    }

    /// <summary>
    /// 零依赖备选：Unity 内置 <c>JsonUtility</c>。更快也更小，但有三个硬限制——
    /// 不支持 <c>Dictionary</c>、不支持多态（基类引用存子类会丢字段）、
    /// 顶层不能直接是 <c>List&lt;T&gt;</c>（必须包一层可序列化类）。
    /// 用之前先确认你的存档数据不触碰这三条。
    /// </summary>
    public sealed class JsonUtilitySaveSerializer : ISaveSerializer
    {
        public string Serialize(object obj)
        {
            if (obj == null) return string.Empty;
            // JsonUtility.ToJson 要求对象本身可序列化；顶层 List 会返回 "{}"，
            // 这里不做补偿——那是使用方该避免的写法。
            return JsonUtility.ToJson(obj);
        }

        public T Deserialize<T>(string text)
        {
            if (string.IsNullOrEmpty(text)) return default;
            // 值类型（struct）JsonUtility.FromJson<T> 走不通，用 FromJson(text, typeof(T)) 兜住
            var t = typeof(T);
            if (t.IsValueType)
            {
                var boxed = JsonUtility.FromJson(text, t);
                return boxed == null ? default : (T)boxed;
            }
            return JsonUtility.FromJson<T>(text);
        }
    }
}

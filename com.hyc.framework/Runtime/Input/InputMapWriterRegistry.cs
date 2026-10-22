using System.Collections.Generic;

namespace HYC.Framework.Input
{
    /// <summary>
    /// 写入器注册表：各 map 的生成写入器在 <c>RuntimeInitializeOnLoadMethod</c> 静态构造里自注册进来。
    /// 框架驱动系统只读这张表，不依赖反射、不依赖跨包编译引用——生成的写入器只要在自己 asmdef 里
    /// 引用 HYC.Framework.Runtime 即可注册，互不耦合。
    /// </summary>
    public static class InputMapWriterRegistry
    {
        static readonly List<IInputMapWriter> Writers = new List<IInputMapWriter>();

        public static void Register(IInputMapWriter w)
        {
            if (w == null || Writers.Contains(w)) return;
            Writers.Add(w);
        }

        public static IReadOnlyList<IInputMapWriter> All => Writers;
    }
}

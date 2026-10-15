namespace HYC.Framework.Config.Server
{
    /// <summary>
    /// 配置服务器侧类型基类（HYC 级别，随包预编译）：承载基层层面字段 ID / GUID。
    /// <para>
    /// 生成的 <c>XxxServer</c> 继承本类（只声明自身字段，ID/GUID 来自这里）；
    /// <c>From</c> 平铺赋值全部导出字段——继承字段经派生引用可直接访问，故无需调用基类 From。
    /// JSON 序列化自动包含继承的公开字段，ID/GUID 随之进入导出产物。
    /// </para>
    /// </summary>
    public class ConfigBaseServer
    {
        public long ID;
        public string GUID;

        /// <summary>从编辑器配置类投影基层层面字段（ID/GUID）。</summary>
        public static ConfigBaseServer From(ConfigBase editor)
        {
            var r = new ConfigBaseServer();
            r.ID = editor.ID;
            r.GUID = editor.GUID;
            return r;
        }
    }
}

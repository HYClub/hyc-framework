using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>
    /// 所有生成配置的默认基类（框架级，随包预编译，不再由代码生成器往宿主工程吐出）。
    /// 仅承载 ID / GUID 两个只读字段，由 ConfigIdService（发号服）经 ConfigAssetFactory /
    /// 「配置创建器」统一派发，禁止手填（手填绕过发号服会与团队冲突，导出前查重会拦）。
    /// <para>
    /// 生成器对 <c>FrameworkTemplates/ConfigBase.asset</c> 设 <c>generateCode=false</c>，
    /// 故不再往宿主生成主类；框架侧手写提供同名支撑类型：
    /// <c>ConfigBaseClient</c> / <c>ConfigBaseServer</c>（侧类型基类，承载 ID/GUID）与
    /// <c>ConfigBaseExport</c>（导出管线基类）。派生配置的主类 / 侧类型 / 导出类分别继承
    /// 这三者，形成与配置链一致的三条继承链。
    /// </para>
    /// </summary>
    public abstract class ConfigBase : ScriptableObject
    {
        [ReadOnly]
        public long ID;

        [ReadOnly]
        public string GUID;
    }
}

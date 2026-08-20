using UnityEngine;

namespace HYC.Framework.Config
{
    /// <summary>文件路径字段：带选择按钮，验证路径存在。</summary>
    public class FileAttribute : PropertyAttribute
    {
        public string ext;
        public FileAttribute(string ext) => this.ext = ext;
    }
}

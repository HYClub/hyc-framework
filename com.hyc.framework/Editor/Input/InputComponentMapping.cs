using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;

namespace HYC.Framework.Editor
{
    /// <summary>单个 action map 的输出配置：生成到哪个文件夹 / 命名空间 / 组件名。</summary>
    [System.Serializable]
    public class InputMapTarget
    {
        /// <summary>InputActionAsset 里对应的 action map 名（必须完全匹配）。</summary>
        public string mapName = "UI";
        /// <summary>生成 .cs 的输出文件夹（相对工程根，例如 Assets/Game/Input/UI）。文件夹归属的 asmdef 即「包」。</summary>
        public string outputFolder = "Assets/Input/UI";
        /// <summary>生成代码的命名空间（例如 Game.Input.UI）。</summary>
        public string ns = "Game.Input.UI";
        /// <summary>组件名；留空 = mapName + "InputComponent"。</summary>
        public string componentName = "";
    }

    /// <summary>
    /// 输入组件映射配置资产。宿主建一个，填：要读的 InputActionAsset + 每个 map 的输出去向。
    /// 菜单 HYC Framework/Input/Generate Input Components 会扫描工程里所有本资产并生成代码。
    /// </summary>
    [CreateAssetMenu(fileName = "InputComponentMapping", menuName = "HYC Framework/Input/Input Component Mapping")]
    public class InputComponentMapping : ScriptableObject
    {
        /// <summary>要按 action 生成的 InputActionAsset（用户在 Unity 编辑器里用 Input System 加 action）。</summary>
        public InputActionAsset asset;
        /// <summary>每个 action map 的生成去向。</summary>
        public List<InputMapTarget> maps = new List<InputMapTarget>();
    }
}

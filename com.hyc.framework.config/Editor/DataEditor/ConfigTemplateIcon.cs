using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Resolves template icons: built-in Unity icon names (EditorGUIUtility.IconContent)
    /// or a custom project Texture2D. Shared by the data tree, create window and
    /// the template editor picker.
    /// </summary>
    public static class ConfigTemplateIcon
    {
        /// <summary>模板生效图标：自定义优先，其次内置名，无则 null（默认图标）。</summary>
        public static Texture Resolve(ConfigTemplate tpl)
        {
            if (tpl == null)
                return null;
            if (tpl.iconCustom != null)
                return tpl.iconCustom;
            return GetBuiltIn(tpl.iconBuiltInName);
        }

        /// <summary>按名字取内置图标（名字需为已验证的规范名），无效返回 null。</summary>
        public static Texture GetBuiltIn(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            try
            {
                var content = EditorGUIUtility.IconContent(name);
                return content?.image;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>常用内置图标名集合（全部已验证有效，供浏览器使用）。</summary>
        public static List<string> GetBuiltInNames()
        {
            return _validated;
        }

        // 已逐个验证有效的规范图标名（EditorGUIUtility.IconContent 可解析）
        private static readonly List<string> _validated = new List<string>
        {
            "GameObject Icon", "d_GameObject Icon", "d_Prefab Icon", "Prefab Icon", "Folder Icon",
            "FolderEmpty Icon", "FolderFavorite Icon", "d_FolderOpened Icon", "d_BoxCollider Icon",
            "Light Icon", "d_Light Icon", "Material Icon", "d_Material Icon", "Mesh Icon", "d_Mesh Icon",
            "Shader Icon", "d_Shader Icon", "Texture Icon", "d_Texture Icon", "Sprite Icon", "d_Sprite Icon",
            "RawImage Icon", "Image Icon", "AudioClip Icon", "d_AudioClip Icon", "AudioSource Icon",
            "AnimationClip Icon", "Animation Icon", "Animator Icon", "AnimatorController Icon", "d_AnimatorController Icon",
            "TimelineAsset Icon", "SpriteRenderer Icon", "Tile Icon", "d_SpriteRenderer Icon",
            "ParticleSystem Icon", "d_ParticleSystem Icon", "TrailRenderer Icon", "Canvas Icon", "d_Canvas Icon",
            "CanvasScaler Icon", "Button Icon", "Text Icon", "Slider Icon", "Toggle Icon", "InputField Icon",
            "Camera Icon", "d_Camera Icon", "d_SceneViewCamera", "cs Script Icon", "d_cs Script Icon",
            "ScriptableObject Icon", "d_ScriptableObject Icon", "Settings Icon", "d_Settings Icon",
            "PlayButton", "PauseButton", "StepButton", "d_PlayButton", "d_PauseButton", "d_StepButton",
            "Search Icon", "d_Search Icon", "BoxCollider Icon", "SphereCollider Icon", "CapsuleCollider Icon",
            "MeshCollider Icon", "Rigidbody Icon", "d_Rigidbody Icon", "BoxCollider2D Icon", "CircleCollider2D Icon",
            "CharacterController Icon", "Terrain Icon", "d_Terrain Icon", "WindZone Icon", "SceneAsset Icon",
            "d_SceneAsset Icon",
        };

        /// <summary>验证内置图标名是否有效（用于选择器勾选状态）。</summary>
        public static bool IsValidBuiltInName(string name)
        {
            return !string.IsNullOrEmpty(name) && GetBuiltIn(name) != null;
        }

        /// <summary>常用内置图标名集合（浏览器的候选源）。</summary>

    }
}

using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HYC.Framework.UI.Editor
{
    /// <summary>
    /// Project-wide settings for the UI binder generator, stored as a
    /// serialized ScriptableObject under ProjectSettings (no asset in Assets/).
    /// </summary>
    public sealed class ComponentBinderSetting : ScriptableObject
    {
        /// <summary>Search every prefab, or only under <see cref="PrefabFolder"/>.</summary>
        public bool FindAllPrefab;

        /// <summary>Prefab folder used when <see cref="FindAllPrefab"/> is false.</summary>
        public DefaultAsset PrefabFolder;

        /// <summary>true = output into a project folder asset; false = output to an absolute path.</summary>
        public bool CodeOutputMethod;

        /// <summary>Project folder asset for generated code (when <see cref="CodeOutputMethod"/>).</summary>
        public DefaultAsset CodeOutputFolder;

        /// <summary>Absolute path for generated code (when !<see cref="CodeOutputMethod"/>).</summary>
        public string CodeOutputFolderPath;

        /// <summary>Default namespace for generated binders.</summary>
        public string CodeOutputPackageName;

        /// <summary>Prefix prepended to every generated class name.</summary>
        public string ClassNamePrefix;

        /// <summary>Suffix appended to every generated class name.</summary>
        public string ClassNameSuffix;

        private const string SAVE_PATH = "ProjectSettings/ComponentBinder.asset";
        private static ComponentBinderSetting s_Instance;

        public static ComponentBinderSetting instance
        {
            get
            {
                if (s_Instance == null || s_Instance.Equals(null)) CreateOrLoad();
                return s_Instance;
            }
        }

        private static ComponentBinderSetting CreateOrLoad()
        {
            var results = InternalEditorUtility.LoadSerializedFileAndForget(SAVE_PATH);
            if (results != null && results.Length > 0) s_Instance = results[0] as ComponentBinderSetting;
            if (s_Instance == null) s_Instance = CreateInstance<ComponentBinderSetting>();
            return s_Instance;
        }

        public static void Save()
        {
            if (s_Instance == null) return;
            var folder = Path.GetDirectoryName(SAVE_PATH);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            InternalEditorUtility.SaveToSerializedFileAndForget(new[] { s_Instance }, SAVE_PATH, true);
        }
    }
}
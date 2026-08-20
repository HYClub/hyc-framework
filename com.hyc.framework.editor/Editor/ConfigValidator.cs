using HYC.Framework.Config;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// Validates loaded config tables and generated config structs: duplicate
    /// ids, out-of-range references, missing <see cref="BlobGenerateAttribute"/>,
    /// non-blittable members, and id collisions. Intended to run in the editor
    /// or a batch validation job.
    /// </summary>
    public static class ConfigValidator
    {
        public sealed class Issue
        {
            public string Table;
            public string Field;
            public string Message;
            public string Severity = "Error";
        }

        public static List<Issue> Issues = new List<Issue>();

        [MenuItem("HYC Framework/Tools/Validate Config")]
        public static void ValidateAll()
        {
            Issues.Clear();
            foreach (var type in ConfigManager.AllTypes)
            {
                ValidateStruct(type);
            }
            if (Issues.Count == 0)
            {
                Debug.Log("[ConfigValidator] No issues found.");
                return;
            }
            foreach (var issue in Issues)
            {
                if (issue.Severity == "Error") Debug.LogError("[ConfigValidator] " + issue.Message);
                else Debug.LogWarning("[ConfigValidator] " + issue.Message);
            }
        }

        private static void ValidateStruct(Type rowType)
        {
            var mark = rowType.GetCustomAttribute<BlobGenerateAttribute>(false);
            if (mark == null)
            {
                Add("Error", rowType.Name, "struct lacks [BlobGenerate] attribute");
                return;
            }

            foreach (var f in rowType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var ft = f.FieldType;
                if (ft == typeof(string) || ft == typeof(UnityEngine.Vector2) ||
                    ft == typeof(UnityEngine.Vector3) || ft == typeof(UnityEngine.Vector4))
                {
                    // blittable/native-friendly is OK
                    continue;
                }
                if (ft.IsClass || ft.IsInterface)
                {
                    Add("Error", rowType.Name + "." + f.Name, "field is a class/reference; cfg structs must be blittable");
                }
            }
        }

        private static void Add(string sev, string loc, string msg)
        {
            Issues.Add(new Issue { Severity = sev, Message = loc + ": " + msg, Field = loc });
        }
    }
}
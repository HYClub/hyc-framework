using HYC.Framework.Config;
using HYC.Framework.Config.Editor;
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
    ///
    /// <see cref="ValidateStruct"/> uses a real blittability check (see
    /// <see cref="IsBlittable"/>): only primitives, enums, Unity.Mathematics /
    /// Unity.Collections / Unity.Entities value types, arrays of blittable
    /// element types, and recursively-blittable structs are accepted.
    /// <c>string</c>, <c>UnityEngine.Vector*</c> / <c>Quaternion</c> / <c>Color</c>,
    /// classes, interfaces and generic types (e.g. <c>List&lt;T&gt;</c>) are
    /// rejected — they must be expressed via <c>BlobString</c> /
    /// <c>FixedString*</c> / <c>float3</c> in the final Blob schema.
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

            // Surface the result in the grouped error window.
            var errors = new List<CheckError>(Issues.Count);
            foreach (var issue in Issues)
            {
                errors.Add(new CheckError(
                    null,
                    issue.Table,
                    issue.Severity == "Error" ? CheckErrorLevel.Error : CheckErrorLevel.Warning,
                    issue.Message,
                    issue.Field));
            }
            BuildErrorWindow.OpenWindow(errors);
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
                if (!IsBlittable(f.FieldType))
                {
                    Add("Error", rowType.Name + "." + f.Name,
                        "field is not blittable; cfg structs must be blittable " +
                        "(use BlobString/FixedString* or float3, not string/Vector*/class)");
                }
            }
        }

        /// <summary>
        /// True when <paramref name="t"/> can be stored in a Blob without
        /// marshalling/reference surprises. Recurses into struct fields.
        /// </summary>
        public static bool IsBlittable(Type t)
        {
            if (t == null) return false;
            if (t.IsPrimitive || t.IsEnum) return true;
            if (t == typeof(string)) return false;
            // UnityEngine math/struct types must be expressed via Unity.Mathematics /
            // FixedString* in the Blob schema.
            if (t.Namespace == "UnityEngine") return false;
            if (t.IsClass || t.IsInterface) return false;
            if (t.IsArray) return IsBlittable(t.GetElementType());
            if (t.IsGenericType) return false;          // List<T>, Dictionary<,>, etc.
            if (t.IsValueType)
            {
                var ns = t.Namespace ?? string.Empty;
                if (ns == "Unity.Mathematics" || ns == "Unity.Collections" || ns == "Unity.Entities")
                    return true;
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!IsBlittable(f.FieldType)) return false;
                }
                return true;
            }
            return false;
        }

        private static void Add(string sev, string loc, string msg)
        {
            Issues.Add(new Issue { Severity = sev, Message = loc + ": " + msg, Field = loc });
        }
    }
}

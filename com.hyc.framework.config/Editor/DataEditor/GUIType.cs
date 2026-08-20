using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 类型的字段元数据缓存：反射扫描所有可序列化字段，按 [Group] 分组，
    /// 按继承层级 + 声明顺序排序。数据编辑器/Inspector 共用。
    /// </summary>
    public class GUIType
    {
        private static readonly Dictionary<Type, GUIType> TYPES = new Dictionary<Type, GUIType>();

        public static GUIType Get(Type type)
        {
            if (!TYPES.TryGetValue(type, out var guiType))
            {
                guiType = new GUIType(type);
                TYPES[type] = guiType;
            }
            return guiType;
        }

        public static void ClearCache() => TYPES.Clear();

        public List<GUITypeField> Fields { get; } = new List<GUITypeField>();

        private readonly Dictionary<string, GUITypeField> FieldTable = new Dictionary<string, GUITypeField>();

        /// <summary>分组名（含默认"基础属性"），按声明顺序。</summary>
        public List<string> Groups { get; } = new List<string>();
        private readonly Dictionary<string, GUITypeField> GroupLevels = new Dictionary<string, GUITypeField>();
        private readonly Dictionary<string, List<GUITypeField>> GroupTable = new Dictionary<string, List<GUITypeField>>();
        private static readonly List<GUITypeField> Empty = new List<GUITypeField>();

        private const string DefaultGroup = "基础属性";

        private GUIType(Type type)
        {
            var level = 0;
            var index = 0;

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
            {
                var inspectorName = field.GetCustomAttributes(typeof(InspectorNameAttribute), true).FirstOrDefault() as InspectorNameAttribute;
                if (field.IsPrivate && inspectorName == null)
                    continue;

                var fieldLevel = GetDefinedLevel(type, field);
                if (level != fieldLevel)
                {
                    level = fieldLevel;
                    index = 0;
                }

                var guiField = new GUITypeField
                {
                    Name = field.Name,
                    Type = field.FieldType,
                    Level = level,
                    Index = index,
                    Label = inspectorName != null ? new GUIContent(inspectorName.displayName) : new GUIContent(field.Name),
                };

                // 分组
                var group = field.GetCustomAttributes(typeof(GroupAttribute), true).FirstOrDefault() as GroupAttribute;
                var groupName = group != null ? group.Name : DefaultGroup;
                if (!GroupTable.TryGetValue(groupName, out var groupList))
                {
                    Groups.Add(groupName);
                    groupList = new List<GUITypeField>();
                    GroupTable[groupName] = groupList;
                }
                if (!GroupLevels.ContainsKey(groupName))
                    GroupLevels[groupName] = guiField;
                groupList.Add(guiField);

                // 其他特性
                guiField.Min = field.GetCustomAttributes(typeof(MinAttribute), true).FirstOrDefault() as MinAttribute;
                guiField.Max = field.GetCustomAttributes(typeof(MaxAttribute), true).FirstOrDefault() as MaxAttribute;
                guiField.HideInInspector = field.GetCustomAttributes(typeof(HideInInspector), true).Length > 0;
                guiField.Visible = field.GetCustomAttributes(typeof(VisibleAttribute), true).FirstOrDefault() as VisibleAttribute;
                guiField.Multiple = field.GetCustomAttributes(typeof(MultilineAttribute), true).Length > 0;
                var spaceAtEnd = field.GetCustomAttributes(typeof(SpaceAtEndAttribute), true).FirstOrDefault() as SpaceAtEndAttribute;
                if (spaceAtEnd != null)
                    guiField.SpaceAtEnd = spaceAtEnd.Space;
                guiField.ReadOnly = field.GetCustomAttributes(typeof(ReadOnlyAttribute), true).Length > 0;
                guiField.FlatDisplay = field.GetCustomAttributes(typeof(FlatDisplayAttribute), true).Length > 0;
                guiField.Line = field.GetCustomAttributes(typeof(LineAttribute), true).Length > 0;
                guiField.IsLocKey = field.GetCustomAttributes(typeof(LocKeyAttribute), true).Length > 0;
                guiField.Infos = field.GetCustomAttributes<InfoBoxAttribute>(true).ToArray();

                Fields.Add(guiField);
                FieldTable[guiField.Name] = guiField;

                index++;
            }

            Fields.Sort(FieldSorter);
            foreach (var group in GroupTable.Values)
                group.Sort(FieldSorter);

            // 分组按首个字段声明顺序排序
            Groups.Sort((a, b) => FieldSorter(GroupLevels[a], GroupLevels[b]));
            GroupLevels.Clear();
        }

        private int FieldSorter(GUITypeField a, GUITypeField b)
        {
            if (a.Level != b.Level)
                return b.Level.CompareTo(a.Level); // 基类字段在前
            return a.Index.CompareTo(b.Index);
        }

        private int GetDefinedLevel(Type type, FieldInfo field)
        {
            var level = 0;
            while (field.DeclaringType != type)
            {
                type = type.BaseType;
                level++;
                if (type == null)
                    break;
            }
            return level;
        }

        public List<GUITypeField> GetGroupBy(string key)
            => GroupTable.TryGetValue(key, out var list) ? list : Empty;

        public GUITypeField GetFieldBy(string key)
            => FieldTable.TryGetValue(key, out var field) ? field : null;
    }
}

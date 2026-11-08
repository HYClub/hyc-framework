namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>
    /// 内容导出的项目级设置。静态字段由 [HYCSettingsGroup] 扫描并持久化到 HYCSettings.asset；
    /// 静态无参 void 方法用于自定义绘制只读的布局自检状态，不提供独立自检菜单项。
    /// </summary>
    public static class ContentExportSettings
    {
        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "构建产物目录", Order = 0)]
        public static string SourceFolder = "Builds/ContentExport/Build";

        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "发布目录", Order = 1)]
        public static string TargetFolder = "Builds/ContentExport/Remote";

        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "CDN URL Root", Order = 2)]
        public static string RemoteUrlRoot = "";

        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "Default Content Set", Order = 3)]
        public static string DefaultSetName = "all";

        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "业务版本号", Order = 4)]
        public static string Version = "v1.0.0";

        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "渠道", Order = 5)]
        public static string Channel = "stable";

        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "灰度比例 (%)", Order = 6)]
        public static int RolloutPercent = 100;

        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/General", Label = "导出后切换 catalogs.bin", Order = 7)]
        public static bool SwitchPointer = true;

        /// <summary>只读状态展示：布局自检不做独立菜单，也不在这里放「重新自检」按钮。</summary>
        [HYC.Framework.Editor.HYCSettingsGroup("Content Export/Mirror Layout", Label = "镜像布局自检", Order = 0)]
        public static void DrawMirrorLayoutStatus()
        {
            var result = ContentExportLayoutCheck.Verify();
            UnityEditor.EditorGUILayout.LabelField("Entities 程序集版本", result.EntitiesVersion);
            UnityEditor.EditorGUILayout.LabelField("官方字段树指纹", result.OfficialFingerprint);
            UnityEditor.EditorGUILayout.LabelField("本地镜像指纹", result.MirrorFingerprint);
            UnityEditor.EditorGUILayout.LabelField("一致状态", result.Matched ? "一致" : "漂移");
            if (!result.Matched)
            {
                UnityEditor.EditorGUILayout.HelpBox(
                    "镜像布局与官方不一致。下次导出会提示并按当前 Entities 版本自动更新镜像 struct。",
                    UnityEditor.MessageType.Warning);
            }
        }

        public static ContentExportOptions CreateOptions(ContentExportScopeRequest scope)
        {
            return new ContentExportOptions
            {
                SourceFolder = ResolveProjectPath(SourceFolder),
                TargetFolder = ResolveProjectPath(TargetFolder),
                Scope = scope ?? new ContentExportScopeRequest(),
                DefaultSetName = string.IsNullOrEmpty(DefaultSetName) ? "all" : DefaultSetName,
                SwitchPointer = SwitchPointer,
                Version = Version,
                Channel = string.IsNullOrEmpty(Channel) ? "stable" : Channel,
                RolloutPercent = RolloutPercent
            };
        }

        /// <summary>工程相对路径 → 绝对路径；已是绝对路径则原样返回。</summary>
        public static string ResolveProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            if (System.IO.Path.IsPathRooted(path))
                return path;
            var root = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path));
        }
    }
}

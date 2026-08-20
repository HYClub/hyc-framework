using System;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 数据编辑器设置：配置根目录、生成目录、生成命名空间。
    /// </summary>
    public class ConfigDataSettingsWindow : EditorWindow
    {
        private string mRootFolder;
        private string mOutputDir;
        private string mNamespace;
        private string mEditorDir;
        private string mClientExportDir;
        private string mServerExportDir;
        private bool mSideTypeIsStruct;
        private int mClientFormat;
        private int mServerFormat;
        private string mBlobOutputDir;
        private string mBlobLoadPath;
        private int mIdProviderType;
        private string mIdServerIp;
        private int mIdServerPort;

        public static void Open()
        {
            var window = GetWindow<ConfigDataSettingsWindow>(true, "数据编辑器设置");
            window.minSize = new Vector2(520, 480);
            window.mRootFolder = ConfigDataSettings.RootFolder;
            window.mOutputDir = ConfigDataSettings.OutputDir;
            window.mNamespace = ConfigDataSettings.Namespace;
            window.mEditorDir = ConfigDataSettings.EditorDir;
            window.mClientExportDir = ConfigDataSettings.ClientExportDir;
            window.mServerExportDir = ConfigDataSettings.ServerExportDir;
            window.mSideTypeIsStruct = ConfigDataSettings.SideTypeIsStruct;
            window.mClientFormat = ConfigDataSettings.ClientFormat;
            window.mServerFormat = ConfigDataSettings.ServerFormat;
            window.mBlobOutputDir = ConfigDataSettings.BlobOutputDir;
            window.mBlobLoadPath = ConfigDataSettings.BlobLoadPath;
            window.mIdProviderType = ConfigDataSettings.IdProviderType;
            window.mIdServerIp = ConfigDataSettings.IdServerIp;
            window.mIdServerPort = ConfigDataSettings.IdServerPort;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("数据编辑器设置", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("配置根目录", GUILayout.Width(120));
            mRootFolder = EditorGUILayout.TextField(mRootFolder);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                var picked = EditorUtility.OpenFolderPanel("选择配置根目录", mRootFolder, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    var rel = picked.Replace('\\', '/');
                    if (rel.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                        mRootFolder = "Assets" + rel.Substring(Application.dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("错误", "配置根目录必须在 Assets 目录内!", "确定");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("生成目录", GUILayout.Width(120));
            mOutputDir = EditorGUILayout.TextField(mOutputDir);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                var picked = EditorUtility.OpenFolderPanel("选择生成目录", mOutputDir, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    var rel = picked.Replace('\\', '/');
                    if (rel.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                        mOutputDir = "Assets" + rel.Substring(Application.dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("错误", "生成目录必须在 Assets 目录内!", "确定");
                }
            }
            EditorGUILayout.EndHorizontal();

            mNamespace = EditorGUILayout.TextField("命名空间", mNamespace);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("编辑器生成目录", GUILayout.Width(120));
            mEditorDir = EditorGUILayout.TextField(mEditorDir);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                var picked = EditorUtility.OpenFolderPanel("选择编辑器生成目录", mEditorDir, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    var rel = picked.Replace('\\', '/');
                    if (rel.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                        mEditorDir = "Assets" + rel.Substring(Application.dataPath.Length);
                    else
                        EditorUtility.DisplayDialog("错误", "编辑器生成目录必须在 Assets 目录内!", "确定");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("导出", EditorStyles.boldLabel);
            mClientExportDir = EditorGUILayout.TextField("客户端导出目录", mClientExportDir);
            mServerExportDir = EditorGUILayout.TextField("服务器导出目录", mServerExportDir);
            mSideTypeIsStruct = EditorGUILayout.Toggle("导出类型为 struct（字段平铺）", mSideTypeIsStruct);

            EditorGUILayout.Space();
            mClientFormat = EditorGUILayout.Popup("客户端导出格式", mClientFormat, new[] { "JSON", "Blob" });
            mServerFormat = EditorGUILayout.Popup("服务器导出格式", mServerFormat, new[] { "JSON" });

            // Blob 相关设置（仅客户端选 Blob 时显示）
            if (mClientFormat == 1)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Blob 输出目录", GUILayout.Width(120));
                mBlobOutputDir = EditorGUILayout.TextField(mBlobOutputDir);
                if (GUILayout.Button("浏览", GUILayout.Width(60)))
                {
                    var picked = EditorUtility.OpenFolderPanel("选择 Blob 输出目录", mBlobOutputDir, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        var rel = picked.Replace('\\', '/');
                        if (rel.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                            mBlobOutputDir = "Assets" + rel.Substring(Application.dataPath.Length);
                        else
                            EditorUtility.DisplayDialog("错误", "Blob 输出目录必须在 Assets 目录内!", "确定");
                    }
                }
                EditorGUILayout.EndHorizontal();
                mBlobLoadPath = EditorGUILayout.TextField("运行时加载路径(相对StreamingAssets)", mBlobLoadPath);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ID 构造器", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("类型", GUILayout.Width(120));
            var newType = EditorGUILayout.Popup(mIdProviderType, new[] { "本地", "局域网" });
            if (newType != mIdProviderType)
            {
                mIdProviderType = newType;
                ConfigIdService.ResetProvider();
            }
            if (GUILayout.Button("检测", GUILayout.Width(60)))
            {
                var ok = mIdProviderType == 1
                    ? new NetworkConfigIdProvider(mIdServerIp, mIdServerPort).Ping()
                    : true;
                EditorUtility.DisplayDialog("检测结果", ok ? "连接正常（绿）" : "连接失败（红）", "确定");
            }
            EditorGUILayout.EndHorizontal();

            if (mIdProviderType == 1)
            {
                mIdServerIp = EditorGUILayout.TextField("服务器 IP", mIdServerIp);
                mIdServerPort = EditorGUILayout.IntField("端口", mIdServerPort);
                if (mIdServerPort < 1)
                    mIdServerPort = 1;
                if (mIdServerPort > 65535)
                    mIdServerPort = 65535;

                if (ConfigIdService.HasServerLauncherPlugin)
                {
                    if (GUILayout.Button("在 Unity 内启动 ID 服务器"))
                        ConfigIdService.LaunchServerPlugin();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "命名空间需为合法 C# 标识符（可用点号分隔）。修改后需重新生成配置类。\n" +
                "编辑器生成目录：生成配置类时同时生成 XxxConfigEditor.cs（继承默认渲染，可自行扩展）。\n" +
                "导出目录：客户端/服务器导出输出位置。struct 模式下子结构体字段平铺。\n" +
                "客户端格式选 Blob：导出二进制 .blob 文件（固定文件名 Cfg{类名}.blob），游戏运行时 System 秒加载 + GetCfgXxx(id)/GetCfgXxxList()。\n" +
                "ID 构造器：本地=进程内发号；局域网=连接独立 ID 服务器（规则一致 {id,guid}，多机唯一）。右下角圆点显示连接状态。",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("保存", GUILayout.Width(80)))
            {
                if (!IsValidNamespace(mNamespace))
                {
                    EditorUtility.DisplayDialog("错误", $"命名空间 {mNamespace} 不合法", "确定");
                    return;
                }
                ConfigDataSettings.RootFolder = mRootFolder;
                ConfigDataSettings.OutputDir = mOutputDir;
                ConfigDataSettings.Namespace = mNamespace;
                ConfigDataSettings.EditorDir = mEditorDir;
                ConfigDataSettings.ClientExportDir = mClientExportDir;
                ConfigDataSettings.ServerExportDir = mServerExportDir;
                ConfigDataSettings.SideTypeIsStruct = mSideTypeIsStruct;
                ConfigDataSettings.ClientFormat = mClientFormat;
                ConfigDataSettings.ServerFormat = mServerFormat;
                ConfigDataSettings.BlobOutputDir = mBlobOutputDir;
                ConfigDataSettings.BlobLoadPath = mBlobLoadPath;
                ConfigDataSettings.IdProviderType = mIdProviderType;
                ConfigDataSettings.IdServerIp = mIdServerIp;
                ConfigDataSettings.IdServerPort = mIdServerPort;
                ConfigIdService.ResetProvider();
                Close();
            }
            if (GUILayout.Button("取消", GUILayout.Width(80)))
            {
                Close();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 右下角 ID 构造器状态点
            ConfigIdService.DrawStatusDot(new Rect(position.width - 18, position.height - 18, 12, 12));
        }

        private static bool IsValidNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns))
                return false;
            var parts = ns.Split('.');
            foreach (var part in parts)
            {
                if (!ConfigTemplateCodeGen.IsValidIdentifier(part))
                    return false;
            }
            return true;
        }
    }
}

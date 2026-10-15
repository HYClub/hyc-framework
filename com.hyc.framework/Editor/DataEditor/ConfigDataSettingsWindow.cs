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
        private string mEditorDir;
        private string mClientExportDir;
        private string mServerExportDir;
        private bool mSideTypeIsStruct;
        private int mClientFormat;
        private int mServerFormat;
        private string mBlobOutputDir;
        private int mIdProviderType;
        private string mIdServerIp;
        private int mIdServerPort;

        public static void Open()
        {
            var window = GetWindow<ConfigDataSettingsWindow>(true, "数据编辑器设置");
            window.minSize = new Vector2(520, 480);
            window.mRootFolder = ConfigDataSettings.RootFolder;
            window.mOutputDir = ConfigDataSettings.OutputDir;
            window.mEditorDir = ConfigDataSettings.EditorDir;
            window.mClientExportDir = ConfigDataSettings.ClientExportDir;
            window.mServerExportDir = ConfigDataSettings.ServerExportDir;
            window.mSideTypeIsStruct = ConfigDataSettings.SideTypeIsStruct;
            window.mClientFormat = ConfigDataSettings.ClientFormat;
            window.mServerFormat = ConfigDataSettings.ServerFormat;
            window.mBlobOutputDir = ConfigDataSettings.BlobOutputDir;
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

            // 生成命名空间不再单独配置：由「生成代码命名空间 root」派生（{root}.config），
            // root 在 HYC Settings 窗口的 CodeGen 分组配（留空=项目公司名→Default）。这里只读展示。
            EditorGUILayout.LabelField("生成命名空间（只读，由 root 派生）");
            EditorGUILayout.LabelField(ConfigDataSettings.Namespace, EditorStyles.miniLabel);

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
                // 运行时加载路径由「Blob 输出目录」派生（只读）：导出与读取永远是同一处，杜绝两边配不一致。
                // 注：用 4 参 LabelField(label, value, style, options) 重载——本仓库已有同形调用可实证；
                //     2 参 (string, string) 重载未在源码中取证，不凭记忆使用。
                EditorGUILayout.LabelField("运行时加载路径(只读, 相对StreamingAssets)",
                                           ConfigDataSettings.ToStreamingAssetsRelative(mBlobOutputDir),
                                           GUI.skin.textField, GUILayout.ExpandWidth(true));
                if (!ConfigDataSettings.IsUnderStreamingAssets(mBlobOutputDir))
                    EditorGUILayout.HelpBox(
                        "Blob 输出目录必须位于 Assets/StreamingAssets 内——运行期只能从那里读取" +
                        "（PC / iOS 直接读文件，WebGL / Android 走预取缓存）。放别处打不进包，运行期必然读空。",
                        MessageType.Error);
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
                "生成命名空间由「生成代码命名空间 root」派生（{root}.config）——root 在 HYC Settings 窗口的 CodeGen 分组配，" +
                "留空 = 项目公司名（Project Settings → Company Name），公司名不可用回落 Default；BT 自定义节点落 {root}.BT.Nodes。\n" +
                "编辑器生成目录：生成配置类时同时生成 XxxConfigEditor.cs（继承默认渲染，可自行扩展）。\n" +
                "导出目录：客户端/服务器导出输出位置。struct 模式下子结构体字段平铺。\n" +
                "客户端格式选 Blob：导出二进制 .blob 文件（固定文件名 Cfg{类名}.blob），游戏运行时 System 秒加载 + GetCfgXxx(id)/GetCfgXxxList()。\n" +
                "Blob 输出目录必须位于 Assets/StreamingAssets 内（运行期加载路径由它自动派生，无需另行配置）；\n" +
                "  PC / iOS 直接读文件，WebGL / Android 由框架自动走「构建列清单 + 启动预取」，游戏层不写平台分支。\n" +
                "ID 构造器：本地=进程内发号；局域网=连接独立 ID 服务器（规则一致 {id,guid}，多机唯一）。右下角圆点显示连接状态。",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("保存", GUILayout.Width(80)))
            {
                if (mClientFormat == 1 && !ConfigDataSettings.IsUnderStreamingAssets(mBlobOutputDir))
                {
                    EditorUtility.DisplayDialog("错误",
                        "客户端格式选了 Blob，但 Blob 输出目录不在 Assets/StreamingAssets 内。\n" +
                        "运行期只能从 StreamingAssets 读取（WebGL / Android 甚至连文件都读不了，只能走预取缓存）。\n" +
                        "请把 Blob 输出目录改到 Assets/StreamingAssets 下的子目录。", "确定");
                    return;
                }
                ConfigDataSettings.RootFolder = mRootFolder;
                ConfigDataSettings.OutputDir = mOutputDir;
                ConfigDataSettings.EditorDir = mEditorDir;
                ConfigDataSettings.ClientExportDir = mClientExportDir;
                ConfigDataSettings.ServerExportDir = mServerExportDir;
                ConfigDataSettings.SideTypeIsStruct = mSideTypeIsStruct;
                ConfigDataSettings.ClientFormat = mClientFormat;
                ConfigDataSettings.ServerFormat = mServerFormat;
                ConfigDataSettings.BlobOutputDir = mBlobOutputDir;
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
        }
    }
}

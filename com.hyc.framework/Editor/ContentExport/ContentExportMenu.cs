namespace HYC.Framework.ContentExport.Editor
{
    /// <summary>HYC 菜单树中的人工入口；AI / CI 走 ContentExportCLI 的 -executeMethod 静态方法，不经菜单。</summary>
    public static class ContentExportMenu
    {
        [UnityEditor.MenuItem("HYC Framework/Tools/ECS Content Export", false, 103)]
        public static void OpenWindow()
        {
            if (UnityEngine.Application.isBatchMode)
            {
                ContentExportLog.Add(ContentExportLogLevel.Warning,
                    "批模式下不打开编辑器窗口；请使用 -executeMethod 调用 ContentExportCLI。");
                return;
            }
            ContentExportWindow.ShowWindow();
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置数据全量检查（移植自 SD 的导出前置检查）：扫描配置根目录下全部配置资产，
    /// 按类型实例化生成的 XxxExport 走 <see cref="ConfigBaseExport.Check"/> 链收集全部错误——
    /// 不落盘任何产物，纯检查。与导出路径共用同一套检查规则与错误窗口。
    /// <para>
    /// 语义与 SD 一致：只有 Error 级算失败，Info/Warning 仅展示；批模式（CI）走 Console。
    /// 除模板字段规则外还含 ID/GUID 查重（SD 全局检查「ID 冲突/ID 完整」的对等物）。
    /// </para>
    /// </summary>
    public static class ConfigCheckService
    {
        // priority=6：Config 档末位（Config Window 0 / Validate 1 / Data Editor 2 / Reissue 3 /
        // Regenerate 4 / Apply Icons 5 → 本项），组内相邻差 1 ⇒ 不画线。
        [MenuItem("HYC Framework/Config/Check All Config Data", false, 6)]
        public static void CheckAllFromMenu() => Run();

        /// <summary>全量检查并展示结果：交互模式 Dialog 概览 + 错误窗口看明细（无错误也开窗展示 Info/Warning）；批模式 Console。</summary>
        public static void Run()
        {
            var errors = CheckAll();

            if (Application.isBatchMode)
            {
                BuildErrorWindow.Report(errors, "检查全部配置");
                if (errors.Count == 0)
                    Debug.Log("[ConfigCheck] 检查全部配置完成：未发现问题");
                return;
            }

            var info = CountLevel(errors, CheckErrorLevel.Info);
            var warn = CountLevel(errors, CheckErrorLevel.Warning);
            var error = CountLevel(errors, CheckErrorLevel.Error);
            var summary = error > 0
                ? $"检查完成：发现 {error} 个错误、{warn} 个警告、{info} 个提示。"
                : $"检查完成：未发现错误（{warn} 个警告、{info} 个提示）。";
            EditorUtility.DisplayDialog("检查全部配置", summary, "确定");
            if (errors.Count > 0)
                BuildErrorWindow.OpenWindow(errors);
        }

        /// <summary>
        /// 扫描配置根目录下全部配置资产并逐类型检查（含 ID/GUID 查重）。
        /// 返回全部错误（Info/Warning/Error 混合，由调用方按级别决定处置）。
        /// </summary>
        public static List<CheckError> CheckAll()
        {
            var errors = new List<CheckError>();
            var assets = ConfigIdService.CollectConfigAssets(ConfigDataSettings.RootFolder);
            if (assets.Count == 0)
            {
                const string msg = "配置根目录下未找到任何配置资产。";
                if (Application.isBatchMode)
                    Debug.Log("[ConfigCheck] " + msg);
                else
                    EditorUtility.DisplayDialog("检查全部配置", msg, "确定");
                return errors;
            }

            // ID/GUID 查重（只读报告；重新派发对话框是导出前交互，不在这里弹）
            foreach (var issue in ConfigIdService.FindIssues(assets))
                errors.Add(new CheckError(issue.Asset, "ID/GUID", CheckErrorLevel.Error, issue.Reason));

            // 按运行时类型分组：派生类型的资产归它自己的导出类（与导出路径同一套归属规则）
            var byType = new Dictionary<System.Type, List<ConfigBase>>();
            foreach (var a in assets)
            {
                if (!(a is ConfigBase cb))
                    continue;
                if (!byType.TryGetValue(cb.GetType(), out var list))
                    byType[cb.GetType()] = list = new List<ConfigBase>();
                list.Add(cb);
            }

            var types = new List<System.Type>(byType.Keys);
            try
            {
                for (var i = 0; i < types.Count; i++)
                {
                    var t = types[i];
                    EditorUtility.DisplayProgressBar("检查全部配置",
                        $"({i + 1}/{types.Count}) {t.Name}", types.Count == 0 ? 0f : (float)i / types.Count);

                    var exportType = ConfigExportService.FindExportType(t);
                    if (exportType == null)
                    {
                        errors.Add(new CheckError(null, t.Name, CheckErrorLevel.Error, "未找到导出类（请先生成代码）"));
                        continue;
                    }
                    // 旧版生成的 XxxExport 是静态类不可实例化——具体原因 TryCreateExporter 已落 Console
                    if (!ConfigExportService.TryCreateExporter(exportType, t, out var exporter))
                    {
                        errors.Add(new CheckError(null, t.Name, CheckErrorLevel.Error,
                            "导出类不可实例化（旧版静态类？），请执行 Regenerate All Config Code"));
                        continue;
                    }

                    foreach (var data in byType[t])
                        errors.AddRange(exporter.Check(data));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return errors;
        }

        private static int CountLevel(List<CheckError> errors, CheckErrorLevel level)
        {
            var n = 0;
            foreach (var e in errors)
                if (e.Level == level)
                    n++;
            return n;
        }
    }
}

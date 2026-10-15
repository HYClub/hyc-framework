using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 配置导出基类（HYC 级别）：负责"导出长什么样"——
    /// ① 导出管线：检查 → 客户端 → 服务器 → <see cref="AssetDatabase.Refresh"/>；
    /// ② 单实例 / 批量两种入口（由 <see cref="ConfigExportService"/> 实例化派生类后调用，
    ///    不走静态反射：继承链上同名静态方法会让 <c>GetMethod</c> 产生歧义）；
    /// ③ JSON 落盘（目录 / 序列化 / 写文件）与 Blob 输出目录校验；
    /// ④ 资产收集（按 <see cref="ConfigType"/> 精确匹配——派生配置类型的资产归它自己的导出类，不混入）。
    /// <para>
    /// 继承链与配置主类链一致：<c>XxxExport : [基类配置的导出类 :] ConfigBaseExport</c>。
    /// 派生类只填类型相关钩子：<see cref="CheckData"/> / <see cref="CreateClientDto"/> /
    /// <see cref="CreateServerDto"/> / <see cref="ClientIsBlob"/> / <see cref="WriteClientBlob"/>。
    /// </para>
    /// </summary>
    public abstract class ConfigBaseExport
    {
        /// <summary>构造时声明本导出类对应的配置类型（基类据此收集资产、命名产物文件）。</summary>
        protected ConfigBaseExport(Type configType)
        {
            ConfigType = configType;
        }

        /// <summary>本导出类对应的配置类型（生成的派生类在构造器里传入）。</summary>
        protected Type ConfigType { get; }

        /// <summary>配置类名（产物文件命名用，如 DropConfig.json）。</summary>
        protected string ClassName => ConfigType.Name;

        /// <summary>导出单个配置实例（生成的派生类经 <see cref="ConfigExportService"/> 转调到此）。</summary>
        public bool ExportCore(ConfigBase data, bool exportClient, bool exportServer)
        {
            var errors = CheckData(data);
            if (errors.Count > 0)
            {
                BuildErrorWindow.OpenWindow(errors);
                return false;
            }

            var ok = true;
            if (exportClient) ok &= ExportClientCore(data);
            if (exportServer) ok &= ExportServerCore(data);
            AssetDatabase.Refresh();
            return ok;
        }

        /// <summary>批量导出该类型全部配置实例（客户端 + 服务器）。返回 true 表示成功。</summary>
        public bool ExportAllCore(bool exportClient, bool exportServer)
        {
            var all = CollectAssets();
            var ok = true;

            if (exportClient)
            {
                if (ClientIsBlob)
                {
                    var dir = ConfigDataSettings.BlobOutputDir;
                    if (string.IsNullOrEmpty(dir) || !ConfigDataSettings.IsBlobOutputDirUnderStreamingAssets)
                    {
                        Debug.LogError("[Config] Blob 导出目录无效（数据编辑器设置 → Blob 输出目录）：" + dir +
                                       "。必须位于 Assets/StreamingAssets 内——运行期只能从那里读取。");
                        return false;
                    }
                    Directory.CreateDirectory(dir);

                    // 先逐个检查（任一行失败即中止、不落盘），再统一构建 Blob 文件
                    for (var i = 0; i < all.Count; i++)
                    {
                        var errs = CheckData(all[i]);
                        if (errs.Count > 0)
                        {
                            BuildErrorWindow.OpenWindow(errs);
                            return false;
                        }
                    }
                    WriteClientBlob(dir, all);
                }
                else
                {
                    var dir = ConfigDataSettings.ClientExportDir;
                    if (string.IsNullOrEmpty(dir)) return false;
                    Directory.CreateDirectory(dir);
                    for (var i = 0; i < all.Count; i++)
                    {
                        var errs = CheckData(all[i]);
                        if (errs.Count > 0)
                        {
                            BuildErrorWindow.OpenWindow(errs);
                            return false;
                        }
                        WriteJson(dir, all[i].name + ".json", CreateClientDto(all[i]));
                    }
                }
            }

            if (exportServer)
            {
                var dir = ConfigDataSettings.ServerExportDir;
                if (string.IsNullOrEmpty(dir)) return false;
                Directory.CreateDirectory(dir);
                var list = new List<object>();
                for (var i = 0; i < all.Count; i++)
                {
                    var errs = CheckData(all[i]);
                    if (errs.Count > 0)
                    {
                        BuildErrorWindow.OpenWindow(errs);
                        return false;
                    }
                    list.Add(CreateServerDto(all[i]));
                }
                WriteJson(dir, ClassName + ".json", list);
            }

            AssetDatabase.Refresh();
            return ok;
        }

        /// <summary>客户端导出：Blob 格式走派生钩子；JSON 格式落盘单个 DTO 到 {ClassName}.json。</summary>
        private bool ExportClientCore(ConfigBase data)
        {
            if (ClientIsBlob)
            {
                var dir = ConfigDataSettings.BlobOutputDir;
                if (string.IsNullOrEmpty(dir) || !ConfigDataSettings.IsBlobOutputDirUnderStreamingAssets)
                {
                    Debug.LogError("[Config] Blob 导出目录无效（数据编辑器设置 → Blob 输出目录）：" + dir +
                                   "。必须位于 Assets/StreamingAssets 内——运行期只能从那里读取。");
                    return false;
                }
                Directory.CreateDirectory(dir);
                WriteClientBlob(dir, new List<ConfigBase> { data });
                return true;
            }

            var jsonDir = ConfigDataSettings.ClientExportDir;
            if (string.IsNullOrEmpty(jsonDir)) return false;
            WriteJson(jsonDir, ClassName + ".json", CreateClientDto(data));
            return true;
        }

        /// <summary>服务器导出（JSON）：单实例写单个对象到 {ClassName}.json（与批量写列表的现有行为保持一致）。</summary>
        private bool ExportServerCore(ConfigBase data)
        {
            var dir = ConfigDataSettings.ServerExportDir;
            if (string.IsNullOrEmpty(dir)) return false;
            WriteJson(dir, ClassName + ".json", CreateServerDto(data));
            return true;
        }

        /// <summary>收集本类型全部配置资产（精确匹配 ConfigType——派生类型的资产归它自己的导出类，不混入）。</summary>
        private List<ConfigBase> CollectAssets()
        {
            var result = new List<ConfigBase>();
            var guids = AssetDatabase.FindAssets("t:" + ConfigType.FullName);
            for (var i = 0; i < guids.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ConfigBase>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (asset == null) continue;
                if (asset.GetType() != ConfigType) continue;
                result.Add(asset);
            }
            return result;
        }

        /// <summary>JSON 落盘：建目录 + 序列化 + 写文件。</summary>
        private static void WriteJson(string dir, string fileName, object payload)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), JsonConvert.SerializeObject(payload, Formatting.Indented));
        }

        // ---------- 派生钩子（生成的 XxxExport 按需覆写） ----------

        /// <summary>
        /// 检查：基类链各自负责自身字段（经 <c>base.CheckData</c> 向上链），派生类补自身字段。
        /// 默认返回空——框架级 ConfigBase 的 ID/GUID 无检查规则（发号/查重由 ConfigIdService 负责）。
        /// </summary>
        protected virtual List<CheckError> CheckData(ConfigBase data) => new List<CheckError>();

        /// <summary>客户端 DTO 投影（侧类型继承链已含基类字段，一次 From 整链投影）。</summary>
        protected abstract object CreateClientDto(ConfigBase data);

        /// <summary>服务器 DTO 投影（同上）。</summary>
        protected abstract object CreateServerDto(ConfigBase data);

        /// <summary>客户端是否走 Blob 格式（生成期按设置烘焙：ClientFormat==1 且有客户端导出字段）。</summary>
        protected virtual bool ClientIsBlob => false;

        /// <summary>Blob 客户端导出（类型相关：BlobBuilder + Cfg{ClassName} 表）。仅 <see cref="ClientIsBlob"/> 为 true 时调用。</summary>
        protected virtual void WriteClientBlob(string dir, IList<ConfigBase> assets) { }
    }
}

# 第三方组件许可（Third-Party Notices）

HYC Framework 的 `Editor/Dlls/` 目录内预置了以下第三方 DLL，**全部仅用于编辑器程序集**
（`Editor/HYC.Framework.Editor.asmdef` 的 `includePlatforms: ["Editor"]`），
不进入任何运行时程序集，也不会被打进玩家包。

> Unity 会自动加载 UPM 包中名为 `Editor/` 的目录，因此在包场景下这些 DLL 不可能泄漏到构建产物。
> 若你把 DLL 手动拷到 `Assets/` 下则不受此保护，请自行留意。

## 组件清单

| DLL | 组件与版本 | 许可证 | 主页 |
| --- | --- | --- | --- |
| `NPOI.dll`<br>`NPOI.OOXML.dll`<br>`NPOI.OpenXml4Net.dll`<br>`NPOI.OpenXmlFormats.dll` | NPOI **2.5.6.0** | Apache License 2.0 | https://github.com/nissl-lab/npoi |
| `ICSharpCode.SharpZipLib.dll` | SharpZipLib **1.3.3** | SharpZipLib License（MIT 变体） | https://www.sharpziplib.com/ |
| `BouncyCastle.Crypto.dll` | Bouncy Castle .NET **2.5.29.19**（`BouncyCastle.Crypto` 程序集名） | MIT License + Bouncy Castle Legacy License（双许可，可任选其一） | https://www.bouncycastle.org/ |

## 为什么需要它们

配置管线（`Editor/DataEditor/`）用 **NPOI** 读写 `.xlsx`：配置表与多语言表的导入导出都走它。
SharpZipLib 与 BouncyCastle 是 NPOI 处理 `.xlsx`（zip + 加密/签名 OOXML）时的传递依赖，
故随 NPOI 一并预置，避免使用方手工装包。

## 升级与替换

这些 DLL 是**手工预置**的，不由 Unity Package Manager 管理。
若要升级版本：直接用同名文件替换，并同步修改
`Editor/HYC.Framework.Editor.asmdef` 的 `precompiledReferences`（若文件名有变）。

## 已知问题

- **BouncyCastle 与 SharpZipLib 都是纯传递依赖**：框架代码不直接引用它们
  （`Editor/` 下零处 `using Org.BouncyCastle` / `ICSharpCode`），
  它们只是 NPOI 处理 `.xlsx` 时的运行期依赖，删掉会导致 Excel 导入在特定场景失败。
- 若你的工程里已有同名 DLL（例如从 NuGet 引入的加密/压缩库），
  可能出现类型冲突报错。此时删除本目录重复的那一份即可。

## 框架自身许可证

见仓库根目录的 `LICENSE`。
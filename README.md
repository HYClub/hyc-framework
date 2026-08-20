# HYC Framework

Unity UPM 私有框架包（HYC 前缀）。由 9 个包组成，按需引用。

## 包列表

| 包 | 说明 |
|---|---|
| `com.hyc.framework.dots` | ECS 基础设施：UpdateGroup 阶梯、单帧消息、Baker 胶水、模拟 |
| `com.hyc.framework.runtime` | 启动/组合、全局管理器、设置、CLI、日志、模拟 |
| `com.hyc.framework.config` | 配置管线：Cfg 结构、模板/枚举/多语言 Key 编辑器、Blob 烘焙、运行时读取 |
| `com.hyc.framework.data` | 数据模型：背包/统计/新物品管线 |
| `com.hyc.framework.loc` | 多语言：Blob 驱动 LocalizationManager、TMP/uGUI 组件、敏感词、编辑器 Excel 导入 |
| `com.hyc.framework.ui` | ECS UI 窗口栈：UIManager/AbsUISystem/BaseWindowSystem/对话框/HUD |
| `com.hyc.framework.input` | 输入层：InputDevice 抽象、热键注册 |
| `com.hyc.framework.timeline` | 配置驱动的过场/Timeline 基类 |
| `com.hyc.framework.editor` | 编辑器套件：设置窗口、资源依赖/清理、配置校验 |

## 安装（UPM，GitHub 私有库）

在用户项目的 `Packages/manifest.json` 中添加（`<tag>` 替换为版本标签，如 `v1.0.0`）：

```json
{
  "dependencies": {
    "com.hyc.framework.dots": "https://github.com/HYClub/hyc-framework.git?path=com.hyc.framework.dots#<tag>",
    "com.hyc.framework.config": "https://github.com/HYClub/hyc-framework.git?path=com.hyc.framework.config#<tag>"
  }
}
```

> 私有仓库：HTTPS 需要 token（`https://<token>@github.com/HYClub/hyc-framework.git?...`），或本机配置好 SSH key 后使用 SSH URL。

## ECS 版本

所有包依赖 `com.unity.entities >= 1.0.16`（宽松区间）。已在 Unity 2022.3 (Entities 1.0.16) 验证；Unity 6.x (Entities 1.4) 请实测，如有 API 差异以 `#if UNITY_6000_0_OR_NEWER` 分版本适配。

## 版本迭代

改包内容后：

```bash
git add -A
git commit -m "feat: ..."
git tag v1.0.1
git push origin main --tags
```

用户项目把 manifest 里的 `#<tag>` 改成新版本号即完成更新（未完成品阶段建议固定 tag，不要跟随 `#main`）。

## 本地开发

包目录直接放进任意 Unity 项目的 `Packages/` 下即可作为本地包使用；开发仓库与发布仓库（本仓库）内容保持同步。

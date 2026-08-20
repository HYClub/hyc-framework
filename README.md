# HYC Framework

Unity UPM 私有框架包（合并单包）。一个包包含全部框架功能。

## 包

`com.hyc.framework` — 合并大包：

- **dots** DOTS 基础设施：UpdateGroup 阶梯、单帧消息、Baker 胶水、模拟
- **runtime** 启动/组合、全局管理器、设置、CLI、日志、模拟
- **config** 配置管线：Cfg 结构、模板/枚举/多语言 Key 编辑器、Blob 烘焙、运行时读取
- **data** 数据模型：背包/统计/新物品管线
- **loc** 多语言：Blob 驱动 LocalizationManager、TMP/uGUI 组件、敏感词、编辑器 Excel 导入
- **ui** ECS UI 窗口栈：UIManager/AbsUISystem/BaseWindowSystem/对话框/HUD
- **input** 输入层：InputDevice 抽象、热键注册
- **timeline** 配置驱动的过场/Timeline 基类
- **editor** 编辑器套件：设置窗口、资源依赖/清理、配置校验

## 安装（UPM，GitHub 私有库）

在用户项目 Package Manager 窗口 → `+` → Add package from git URL：

```
https://github.com/HYClub/hyc-framework.git?path=com.hyc.framework#v1.0.0
```

或 SSH：

```
git@github.com:HYClub/hyc-framework.git?path=com.hyc.framework#v1.0.0
```

> 私有仓库：HTTPS 需要 token（`https://<token>@github.com/...`）或本机 SSH key。

## ECS 版本

依赖 `com.unity.entities >= 1.0.16`（宽松区间）。已在 Unity 2022.3 (Entities 1.0.16) 验证；Unity 6.x (Entities 1.4) 如遇 API 差异，以 `#if UNITY_6000_0_OR_NEWER` 分版本适配。

## 版本迭代

```bash
git add -A
git commit -m "feat: ..."
git tag v1.0.x
git push origin main --tags
```

用户项目把 URL 里的 `#v1.0.x` 改为新版本号即更新。

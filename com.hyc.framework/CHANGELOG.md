# Changelog

All notable changes to the HYC Framework package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [2.0.4]

### Added
- 行为树（BT）编辑器与运行时解释器：可视化 `BTTreeAsset` 编辑、`BTRootBlob` 序列化、`BTManager` 注册、`BTInterpreterSystem` 逐帧解释执行、运行时黑板（`BTBlackboardRuntime`）、自定义节点（`BTCustomNode` + `BTNodeRuntimeRegistry` 反射扫描）、断点调试。
- 配置管线扩展：新增配置字段类型（行为树引用、Addressable、多语言 Key、引用、枚举等）。

### Changed
- 热键系统改用 action-name 字符串（`HotkeyActionNames`）分发，取代原 `HotkeyID` 枚举。
- 升级为可发布的 Unity 包：完善 `package.json` 发布字段（documentationUrl / changelogUrl / licensesUrl），补充 README / LICENSE / CHANGELOG。

## [2.0.3] 及更早

- DOTS / ECS 运行时基建（`Bootstrap` / `Simulation` / 分层 `UpdateGroup`）、消息系统。
- 配置管线（Excel / 模板 → Blob）、多语言本地化（含敏感词）。
- ECS UI 系统（`UIManager` / `AbsUISystem` / `ComponentBinderTable`）。
- 输入 / 热键、Timeline 过场。
- 编辑器工具链（配置生成、组件绑定代码生成、本地化 Key 管理）。

# HYC Framework 使用文档站

`com.hyc.framework` 的公开使用文档站点。纯静态 HTML / CSS / JS，无构建依赖，发布到 GitHub Pages。

线上地址：<https://gameframework.huoyue.club/>（首次启用见下方「GitHub Pages 配置」）。

## 为什么站点在仓库根而不在包里

Unity 官方对 UPM 包文档分发的设计是：**包内只带轻量 markdown**（`Documentation~`，`~` 后缀让 Unity 忽略它、不导入用户工程），**重文档/网页放外部网站**，由 `package.json` 的 `documentationUrl` 指过去。

- 本站点如果放进 `com.hyc.framework/docs-site/`，该目录名没有 `~` 隐藏语义，用户装包时会**真的出现在工程里**——这是必须搬出包的硬理由。
- 官方文档（upm-manifestPkg.html）明确认可 "create the documentation in HTML and host it on your own website"。
- 同生态先例：MagicOnion（Cysharp）把 Docusaurus 文档站放在仓库根 `docs/`，走 GitHub Pages。

`com.hyc.framework/package.json` 已配置：

```json
"documentationUrl": "https://gameframework.huoyue.club/"
```

用户在 Unity Package Manager 里点 "View documentation" 即跳转本站。

## GitHub Pages 配置（一次性）

1. 仓库页面 **Settings → Pages**（或新版 **Settings → Code and automation → Pages**）。
2. **Source** 选 `Deploy from a branch`。
3. Branch 选 `main`，文件夹选 **`/docs`**，保存。
4. 等约 1 分钟，访问 <https://gameframework.huoyue.club/>。

> GitHub Pages 的分支部署源只支持仓库根的 `/` 或 `/docs` 两种，这就是站点必须落在 `docs/` 的原因。一个仓库只能有一个 Pages 站点，所以框架站、游戏站等不要抢同一个仓库。

## 自定义域名 gameframework.huoyue.club

`gameframework.huoyue.club` 是 `huoyue.club` 下的子域，走 CNAME 记录（子域方案最稳，不受 GitHub 服务器 IP 变动影响）。

1. **仓库里**：`docs/CNAME` 已提交，内容就一行 `gameframework.huoyue.club`（发布源是 `/docs`，CNAME 位于发布源根部——官方规则：「custom domains are stored in a CNAME file in the root of your publishing source」）。
2. **DNS 提供商**（huoyue.club 的解析处，腾讯云 DNSPod / 阿里云等）加一条记录：

   | 主机记录 | 记录类型 | 值 |
   |---|---|---|
   | `gameframework` | `CNAME` | `hyclub.github.io` |

   注意 CNAME 值必须是**组织名** `hyclub.github.io`，不带仓库名、不指向 `*.pages.github.io`（官方明确警告）。
3. **GitHub 侧**：Settings → Pages → **Custom domain** 填 `gameframework.huoyue.club` → Save → 等 DNS check 通过（最长 24 小时）→ 勾选 **Enforce HTTPS**（Let's Encrypt 自动签证书，最长 24 小时）。
4. `package.json` 的 `documentationUrl` 已指向 `https://gameframework.huoyue.club/`（站点根 = 门户首页，门户上有显眼的「查看文档」按钮进 `/docs/`）。

⚠️ 别配通配符 `*.huoyue.club`（域名接管风险）；域名必须在全 GitHub Pages 唯一。GitHub 默认域名 `hyclub.github.io/hyc-framework` 始终可达，可作备用入口。

## 本地预览

```bash
# 任选其一
python -m http.server 8080 -d docs
npx serve docs
```

然后打开 <http://localhost:8080>。直接双击 HTML 也能看（站点不依赖服务端），但用 HTTP 服务更接近线上。

## 目录结构

一个 Pages 站点、两副面孔：**站点根 = 门户首页，`/docs/` = 文档**。

```
docs/                        ← Pages 发布源 = 站点根
├── index.html               门户首页（域名/ 落地）：hero + 能力卡片 + 三行代码 + GitHub 入口
├── README.md                本文件（维护说明，不上页面）
├── assets/
│   ├── style.css            共享样式（CSS 变量亮/暗双主题 + 门户/文档两套组件）
│   ├── site.js              共享脚本（导航 / 搜索 / 主题 / TOC / 代码块增强）
│   └── vendor/              highlight.js 本地副本（core + csharp + github-dark 主题）
│       ├── highlight.min.js
│       ├── csharp.min.js
│       └── github-dark.min.css
└── docs/                    33 页文档（域名/docs/ 落地）
    ├── index.html           文档总览（框架总览）
    ├── architecture.html    架构与数据流（新人第一篇）
    ├── ops.html             操作手册：按任务查配方
    ├── bootstrap.html       引导与世界
    ├── samples.html         示例集 HYCSimple 总览
    ├── config.html          配置系统
    ├── enum.html            枚举配置
    ├── localization.html    多语言
    ├── attributes.html      通用属性 Attr
    ├── scene.html           场景内容物
    ├── talent.html          天赋树
    ├── reddot.html          红点树
    ├── save.html            存档存储
    ├── bt.html              行为树
    ├── sync.html            帧同步（P1）
    ├── ui.html              ECS UI 总览
    ├── ui-window.html       窗口系统
    ├── ui-binder.html       组件绑定
    ├── input.html           输入与热键
    ├── cinemachine.html     Cinemachine 相机
    ├── timeline.html        过场 Timeline
    ├── audio.html           音频
    ├── messages.html        消息与日志
    ├── tools.html           编辑器工具链
    ├── settings.html        项目设置
    ├── blob.html            Blob 与数据约定
    └── example-*.html       7 个示例模块页
```

URL 对照：`域名/` = 门户 · `域名/docs/` = 文档总览 · `域名/docs/config.html` = 各文档页。
`package.json` 的 `documentationUrl` 指向站点根（门户），门户上有显眼的「查看文档」按钮进 `/docs/`。

## 页面形态（对齐 Unity 文档站）

- **顶栏**（固定深色）：品牌 + 全局搜索框（`/` 聚焦，↑↓ 选择，回车跳转）+ 版本徽标 + GitHub 图标 + 主题切换。移动端搜索变为全宽浮层。
- **左侧导航**：6 个分组，组标题可点击折叠（状态存 `localStorage`），当前页高亮。
- **内容区**：JS 自动注入面包屑、移动端「本页目录」折叠面板、底部「上一页 / 下一页」+「在 GitHub 上编辑此页」。
- **右侧目录**：`h2/h3` 自动生成，滚动高亮；≤1200px 隐藏，改用内容区顶部的折叠面板。
- **底部**：四栏站点导航（文档 / 仓库 / 相关技术）+ 深色版权条；右下角浮动「回到顶部」。

这些结构都由 `site.js` 注入，写页面时只需提供正文；换页头/页脚样式改 `assets/style.css` 一处即可。
门户页（`index.html`）是特例：不挂 `.content` 容器、不放侧栏，注入逻辑自动全部跳过，只保留主题 / 代码高亮 / 年份。

## 新增一个页面

1. 复制 `docs/docs/` 下任意现有页面改内容（保持 `<head>` 里的 `../assets/style.css` + `../assets/vendor/github-dark.min.css`，和 `<body>` 的 header / sidebar / toc / footer 骨架不变）。
2. 在 `assets/site.js` 的 `NAV` 数组对应分组里加一项：`{ href: "xxx.html", label: "..." }`。**NAV 是侧栏、搜索、面包屑、上下页的唯一真源**，33 个页面不各自维护导航。
3. 需要从首页进，再在 `docs/docs/index.html` 的 `card-grid` 里加一张卡片；要上面孔门户，改 `docs/index.html` 的能力卡片。

注意：`site.js` 会自动为页面里所有 `<pre><code class="language-xxx">` 包上代码块壳（语言标签 + 复制按钮 + 高亮），写页面时不需要手写这些结构。highlight.js 已改本地引用（不依赖 CDN，国内访问更稳），新增语言需下载对应 `languages/*.min.js` 到 `assets/vendor/` 并在页面里引用。

## 约定

- **内容铁律**：文档只写源码实证过的东西。实现状态如实标注（例：Timeline 可视化编辑器未开发、Sync 仅 P1 落地），不写规划中的能力。
- 代码块语言用 `language-csharp` / `language-json` / `language-bash`，`site.js` 会识别并显示标签。
- 页面标题、导航标签用中文；HTML 元信息 `lang="zh-CN"`。
- 改 `package.json` 的 `documentationUrl` 时同步改本文件顶部的线上地址。

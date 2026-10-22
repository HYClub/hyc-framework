/* ============================================================
   HYC Framework Docs — Site script
   Layout mirrors the Unity documentation site.
   - Sidebar nav from NAV (single source of truth), collapsible groups
   - Global search over the nav index (desktop inline / mobile overlay)
   - Breadcrumbs + prev/next + "edit on GitHub" injected per page
   - Mobile "on this page" collapsible panel
   - Theme (light/dark) with localStorage persistence
   - Right-hand TOC generated from h2/h3 with scroll-spy
   - Copy buttons + highlight.js init
   ============================================================ */

const NAV = [
  {
    title: "入门",
    items: [
      { href: "index.html", label: "框架总览" },
      { href: "architecture.html", label: "架构与数据流" },
      { href: "ops.html", label: "操作手册（按任务）" },
      { href: "bootstrap.html", label: "引导与世界" },
      { href: "samples.html", label: "示例集 HYCSimple" },
    ],
  },
  {
    title: "数据系统",
    items: [
      { href: "config.html", label: "配置系统 Config" },
      { href: "enum.html", label: "枚举配置" },
      { href: "localization.html", label: "多语言 Localization" },
      { href: "attributes.html", label: "通用属性 Attr" },
      { href: "scene.html", label: "场景内容物 SceneContent" },
      { href: "talent.html", label: "天赋树 Talent" },
      { href: "reddot.html", label: "红点树 RedDot" },
      { href: "save.html", label: "存档存储 Save" },
    ],
  },
  {
    title: "逻辑",
    items: [
      { href: "bt.html", label: "行为树 BT" },
      { href: "sync.html", label: "帧同步 Sync" },
    ],
  },
  {
    title: "表现",
    items: [
      { href: "ui.html", label: "ECS UI 总览" },
      { href: "ui-window.html", label: "窗口系统" },
      { href: "ui-binder.html", label: "组件绑定" },
      { href: "input.html", label: "输入与热键" },
      { href: "cinemachine.html", label: "Cinemachine 相机" },
      { href: "timeline.html", label: "过场 Timeline" },
      { href: "audio.html", label: "音频 Audio" },
      { href: "messages.html", label: "消息与日志" },
    ],
  },
  {
    title: "工具与约定",
    items: [
      { href: "tools.html", label: "编辑器工具链" },
      { href: "settings.html", label: "项目设置" },
      { href: "blob.html", label: "Blob 与数据约定" },
    ],
  },
  {
    title: "示例（逐个模块）",
    items: [
      { href: "example-config.html", label: "示例 · 配置 Config" },
      { href: "example-attr.html", label: "示例 · 属性 Attr" },
      { href: "example-bt.html", label: "示例 · 行为树 BT" },
      { href: "example-loc.html", label: "示例 · 多语言 Loc" },
      { href: "example-scene.html", label: "示例 · 场景引导" },
      { href: "example-input.html", label: "示例 · 输入组件化" },
      { href: "example-cinemachine.html", label: "示例 · Cinemachine 双屏" },
    ],
  },
];

const REPO = "https://github.com/HYClub/hyc-framework";
const COLLAPSE_KEY = "hyc-nav-collapsed";

/* ---------- Safe storage ----------
   Sandboxed iframes (preview panels) / Safari private mode / storage-disabled
   browsers throw on localStorage access. Never let that kill init. */
const store = {
  get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
  set(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* ignore */ } },
};

/* flat index for search / prev-next */
const FLAT = [];
NAV.forEach((g) => g.items.forEach((it) => FLAT.push({ ...it, group: g.title })));

function currentFile() {
  return location.pathname.split("/").pop() || "index.html";
}

/* ---------- Sidebar ---------- */
function renderSidebar() {
  const el = document.getElementById("nav");
  if (!el) return;
  const cur = currentFile();
  let collapsed = [];
  try { collapsed = JSON.parse(store.get(COLLAPSE_KEY) || "[]"); } catch (e) {}
  let html = "";
  for (const g of NAV) {
    const isCollapsed = collapsed.includes(g.title) ? " collapsed" : "";
    html += `<div class="nav-group${isCollapsed}">`;
    html += `<button class="nav-group-title" type="button" data-title="${g.title}" aria-expanded="${!isCollapsed}">${g.title}<span class="chev">▾</span></button>`;
    for (const it of g.items) {
      const active = it.href === cur ? " active" : "";
      html += `<a class="nav-item${active}" href="${it.href}">${it.label}</a>`;
    }
    html += `</div>`;
  }
  el.innerHTML = html;

  el.querySelectorAll(".nav-group-title").forEach((btn) => {
    btn.addEventListener("click", () => {
      const group = btn.closest(".nav-group");
      const title = btn.dataset.title;
      group.classList.toggle("collapsed");
      btn.setAttribute("aria-expanded", String(!group.classList.contains("collapsed")));
      let saved = [];
      try { saved = JSON.parse(store.get(COLLAPSE_KEY) || "[]"); } catch (e) {}
      if (group.classList.contains("collapsed")) {
        if (!saved.includes(title)) saved.push(title);
      } else {
        saved = saved.filter((t) => t !== title);
      }
      store.set(COLLAPSE_KEY, JSON.stringify(saved));
    });
  });
}

/* ---------- Theme ---------- */
function initTheme() {
  const saved = store.get("hyc-theme");
  const sys = window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  document.documentElement.setAttribute("data-theme", saved || sys);
  updateThemeIcon();
}
function updateThemeIcon() {
  const btn = document.getElementById("themeToggle");
  if (!btn) return;
  btn.textContent = document.documentElement.getAttribute("data-theme") === "dark" ? "☀" : "☾";
}
function toggleTheme() {
  const cur = document.documentElement.getAttribute("data-theme");
  const next = cur === "dark" ? "light" : "dark";
  document.documentElement.setAttribute("data-theme", next);
  store.set("hyc-theme", next);
  updateThemeIcon();
}

/* ---------- Mobile drawer ---------- */
function initDrawer() {
  const sb = document.getElementById("sidebar");
  const bd = document.getElementById("sidebarBackdrop");
  const ham = document.getElementById("hamburger");
  if (ham) ham.addEventListener("click", () => {
    sb.classList.toggle("open");
    bd.classList.toggle("show");
  });
  if (bd) bd.addEventListener("click", () => {
    sb.classList.remove("open");
    bd.classList.remove("show");
  });
}

/* ---------- Global search ---------- */
function setupSearch() {
  const header = document.querySelector(".site-header");
  const input = document.getElementById("searchInput");
  const results = document.getElementById("searchResults");
  const toggle = document.getElementById("searchToggle");
  if (!header || !input || !results) return;
  let sel = -1;

  function render(q) {
    const query = q.trim().toLowerCase();
    if (!query) { results.classList.remove("open"); results.innerHTML = ""; sel = -1; return; }
    const hits = FLAT.filter((it) =>
      it.label.toLowerCase().includes(query) || it.group.toLowerCase().includes(query)
    );
    if (hits.length === 0) {
      results.innerHTML = `<div class="sr-empty">没有匹配「${q.replace(/</g, "&lt;")}」的页面</div>`;
    } else {
      results.innerHTML = hits.map((it, i) =>
        `<a class="sr-item${i === sel ? " sel" : ""}" href="${it.href}"><span class="sr-group">${it.group}</span><span>${it.label}</span></a>`
      ).join("");
    }
    results.classList.add("open");
  }

  input.addEventListener("input", () => { sel = -1; render(input.value); });
  input.addEventListener("keydown", (e) => {
    const items = results.querySelectorAll(".sr-item");
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      if (items.length === 0) return;
      sel = e.key === "ArrowDown" ? Math.min(sel + 1, items.length - 1) : Math.max(sel - 1, 0);
      items.forEach((el, i) => el.classList.toggle("sel", i === sel));
      items[sel].scrollIntoView({ block: "nearest" });
    } else if (e.key === "Enter") {
      const target = sel >= 0 ? items[sel] : items[0];
      if (target) { location.href = target.getAttribute("href"); }
    } else if (e.key === "Escape") {
      input.blur(); results.classList.remove("open");
    }
  });
  input.addEventListener("focus", () => { if (input.value.trim()) render(input.value); });
  document.addEventListener("click", (e) => {
    if (!e.target.closest(".header-search")) results.classList.remove("open");
  });

  if (toggle) toggle.addEventListener("click", () => {
    header.classList.toggle("search-open");
    if (header.classList.contains("search-open")) setTimeout(() => input.focus(), 60);
    else results.classList.remove("open");
  });
  // "/" focuses search on desktop
  document.addEventListener("keydown", (e) => {
    if (e.key === "/" && document.activeElement !== input && !/input|textarea/i.test(document.activeElement.tagName)) {
      e.preventDefault();
      input.focus();
    }
  });
}

/* ---------- Breadcrumbs (injected) ---------- */
function injectBreadcrumbs() {
  const content = document.querySelector(".content");
  if (!content) return;
  const cur = currentFile();
  const entry = FLAT.find((it) => it.href === cur);
  if (!entry) return;
  const nav = document.createElement("nav");
  nav.className = "breadcrumbs";
  nav.setAttribute("aria-label", "面包屑");
  nav.innerHTML =
    `<a href="index.html">首页</a><span class="sep">/</span>` +
    `<span>${entry.group}</span><span class="sep">/</span>` +
    `<span class="current">${entry.label}</span>`;
  content.insertBefore(nav, content.firstChild);
}

/* ---------- Mobile TOC (injected <details>) ---------- */
function collectHeadings() {
  return Array.from(document.querySelectorAll(".content h2, .content h3"));
}
function slugify(s) {
  return s.trim().toLowerCase()
    .replace(/[^\w一-龥]+/g, "-").replace(/^-+|-+$/g, "");
}
function assignHeadingIds(heads) {
  const seen = {};
  heads.forEach((h) => {
    let id = h.id || slugify(h.textContent);
    if (seen[id]) id += "-" + seen[id];
    seen[id] = (seen[id] || 0) + 1;
    h.id = id;
  });
}

function injectMobileTOC(heads) {
  const content = document.querySelector(".content");
  if (!content || heads.length === 0) return;
  const details = document.createElement("details");
  details.className = "toc-mobile";
  const body = heads.map((h) =>
    `<a class="${h.tagName === "H3" ? "toc-h3" : ""}" href="#${h.id}">${h.textContent}</a>`
  ).join("");
  details.innerHTML = `<summary>本页目录</summary><div class="tm-body">${body}</div>`;
  // place after h1 (+ its lead/subtitle if directly following)
  const h1 = content.querySelector("h1");
  let anchor = h1 || content.firstChild;
  let node = anchor ? anchor.nextSibling : null;
  while (node && node.nodeType === 1 && (node.classList.contains("lead") || node.classList.contains("subtitle"))) {
    anchor = node;
    node = node.nextSibling;
  }
  if (anchor && anchor.parentNode === content) anchor.after(details);
  else content.insertBefore(details, content.firstChild);
  // clicking a link should collapse the panel
  details.querySelectorAll("a").forEach((a) => a.addEventListener("click", () => { details.open = false; }));
}

/* ---------- Right TOC + scroll-spy ---------- */
function buildTOC(heads) {
  const wrap = document.getElementById("toc");
  if (!wrap) return;
  if (heads.length === 0) { wrap.style.display = "none"; return; }
  wrap.innerHTML = `<div class="toc-title">本页目录</div>` + heads.map((h) =>
    `<a class="${h.tagName === "H3" ? "toc-h3" : ""}" href="#${h.id}" data-toc="${h.id}">${h.textContent}</a>`
  ).join("");

  const links = wrap.querySelectorAll("a[data-toc]");
  const obs = new IntersectionObserver((entries) => {
    entries.forEach((e) => {
      if (e.isIntersecting) {
        links.forEach((l) => l.classList.remove("active"));
        const a = wrap.querySelector(`a[data-toc="${e.target.id}"]`);
        if (a) a.classList.add("active");
      }
    });
  }, { rootMargin: "-80px 0px -70% 0px" });
  heads.forEach((h) => obs.observe(h));
}

/* ---------- Prev / next + edit link (injected) ---------- */
function injectPageNav() {
  const content = document.querySelector(".content");
  if (!content) return;
  const cur = currentFile();
  const idx = FLAT.findIndex((it) => it.href === cur);
  if (idx === -1) return;
  const prev = FLAT[idx - 1];
  const next = FLAT[idx + 1];

  const foot = document.createElement("div");
  foot.className = "page-foot";
  const edit = document.createElement("a");
  edit.className = "edit-link";
  edit.href = `${REPO}/edit/main/docs/docs/${cur}`;
  edit.target = "_blank";
  edit.rel = "noopener";
  edit.textContent = "在 GitHub 上编辑此页 ↗";
  foot.appendChild(edit);

  const nav = document.createElement("nav");
  nav.className = "page-nav";
  nav.innerHTML =
    (prev
      ? `<a class="prev" href="${prev.href}"><span class="pn-label">‹ 上一页</span><span class="pn-title">${prev.label}</span></a>`
      : `<a class="prev disabled" aria-hidden="true"><span class="pn-label">‹ 上一页</span><span class="pn-title">已是第一页</span></a>`) +
    (next
      ? `<a class="next" href="${next.href}"><span class="pn-label">下一页 ›</span><span class="pn-title">${next.label}</span></a>`
      : `<a class="next disabled" aria-hidden="true"><span class="pn-label">下一页 ›</span><span class="pn-title">已是最后一页</span></a>`);
  foot.appendChild(nav);
  content.appendChild(foot);
}

/* ---------- Back to top ---------- */
function initBackTop() {
  const btn = document.getElementById("backTop");
  if (!btn) return;
  const onScroll = () => btn.classList.toggle("show", window.scrollY > 600);
  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();
  btn.addEventListener("click", () => window.scrollTo({ top: 0, behavior: "smooth" }));
}

/* ---------- Code blocks ---------- */
function enhanceCode() {
  document.querySelectorAll("pre > code").forEach((code) => {
    const pre = code.parentElement;
    const m = (code.className || "").match(/language-([\w+#]+)/);
    const lang = m ? m[1] : "text";
    const block = document.createElement("div");
    block.className = "code-block";
    const head = document.createElement("div");
    head.className = "code-head";
    const langSpan = document.createElement("span");
    langSpan.className = "code-lang";
    langSpan.textContent = lang;
    const btn = document.createElement("button");
    btn.className = "copy-btn";
    btn.textContent = "复制";
    btn.addEventListener("click", () => {
      navigator.clipboard.writeText(code.textContent).then(() => {
        btn.textContent = "已复制";
        btn.classList.add("copied");
        setTimeout(() => { btn.textContent = "复制"; btn.classList.remove("copied"); }, 1500);
      });
    });
    head.appendChild(langSpan);
    head.appendChild(btn);
    pre.parentNode.insertBefore(block, pre);
    block.appendChild(head);
    block.appendChild(pre);
  });
  if (window.hljs) document.querySelectorAll("pre code").forEach((c) => {
    try { window.hljs.highlightElement(c); } catch (e) {}
  });
}

/* ---------- Year ---------- */
function fillYear() {
  document.querySelectorAll("#year").forEach((y) => { y.textContent = new Date().getFullYear(); });
}

/* ---------- Boot ----------
   Each step is isolated: one failure must never take down the rest
   (sandboxed previews, blocked storage, odd markup...). */
function bootStep(fn, name) {
  try { fn(); } catch (e) { console.warn("[hyc-docs] " + name + " skipped:", e); }
}

document.addEventListener("DOMContentLoaded", () => {
  bootStep(renderSidebar, "renderSidebar");
  bootStep(initTheme, "initTheme");
  bootStep(initDrawer, "initDrawer");
  bootStep(setupSearch, "setupSearch");
  bootStep(injectBreadcrumbs, "injectBreadcrumbs");
  bootStep(() => {
    const heads = collectHeadings();
    assignHeadingIds(heads);
    injectMobileTOC(heads);
    buildTOC(heads);
  }, "toc");
  bootStep(injectPageNav, "injectPageNav");
  bootStep(enhanceCode, "enhanceCode");
  bootStep(initBackTop, "initBackTop");
  bootStep(fillYear, "fillYear");
  bootStep(() => {
    const t = document.getElementById("themeToggle");
    if (t) t.addEventListener("click", toggleTheme);
  }, "themeToggle");
});

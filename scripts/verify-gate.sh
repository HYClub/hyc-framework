#!/usr/bin/env bash
# ============================================================
# HYC Framework —— 本地/CI 通用闸门（不依赖 Unity）
#
# 为什么要有这个：
#   Unity batchmode CI 需要 Unity 许可 + 激活密钥，公开仓库的 Actions 上
#   拿不到（Unity 对私有/公开仓库的许可策略不同，且个人版不给密钥）。
#   所以把"不需要 Unity 就能查的硬约束"抽成本脚本——
#   它只用 bash + grep/sed，任何环境都能跑，且能在提交前本地跑。
#
# 它守什么（都是"文档/版本与磁盘脱节"这类静默腐化）：
#   1. 文件索引里的 .cs 数与类型数 == 磁盘实际
#   2. 索引各分组标题的文件数之和 == 总声明数
#   3. 最新 tag 的版本号 == package.json 的 version
#   4. 新增 .cs 是否漏了 .meta（Unity 靠 .meta 记GUID，漏了会重新生成 =引用丢失）
#   5. 包内 .cs 是否存在中文主菜单路径（菜单英文化是既定约定）
#   6. 非泛型声明是否带了 where 约束（CS0080，沙箱禁 csc 时只能静态抓）
#   7. [InitializeOnLoad] 是否修饰在 class 上（CS0592，同上）
#
# 用法：
#   bash scripts/verify-gate.sh
#   bash scripts/verify-gate.sh --skip-tag   # 忽略 tag 检查（发版中途用）
#
# 退出码：0 全过；1 有失败项。
# ============================================================

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

SKIP_TAG=0
for arg in "$@"; do
  case "$arg" in
    --skip-tag) SKIP_TAG=1 ;;
    -h|--help) sed -n '2,30p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "未知参数: $arg（用 --help 看用法）" >&2; exit 2 ;;
  esac
done

PKG="com.hyc.framework"
INDEX="com.hyc.framework/Documentation~/100-文件与类索引.md"

fail=0
pass() { printf '  \033[32m[PASS]\033[0m %s\n' "$1"; }
bad()  { printf '  \033[31m[FAIL]\033[0m %s\n' "$1"; fail=1; }
head_() { printf '\n\033[1m%s\033[0m\n' "$1"; }

if [ ! -d "$PKG" ]; then
  echo "错误：未找到包目录 $PKG，请在仓库根运行本脚本。" >&2
  exit 2
fi

# 类型声明计数。
# ⚠️ 必须先剥离注释与字符串字面量，否则 XML 文档注释里的 `<c>class Foo</c>`
#    会被计进去（实测会虚高 20+）。这个坑踩过两次：索引自检测试与本脚本都中过招。
#    纯 bash 做词法分析不现实，这里用一个内嵌 python3（若缺 python3 则明确降级报错，
#    而不是给出一个偏大的数字——宁可失败也不要给假绿）。
count_types() {
  local py
  py=$(command -v python3 || command -v python || command -v py) || {
    echo "count_types_error" >&2; return; }
  "$py" - "$PKG" <<'PYEOF'
import sys, re, pathlib
pkg = pathlib.Path(sys.argv[1])
def strip_cs(src):
    out=[]; i=0; n=len(src)
    while i<n:
        c=src[i]; nx=src[i+1] if i+1<n else ''
        if c=='/' and nx=='/':
            while i<n and src[i]!='\n': i+=1
            continue
        if c=='/' and nx=='*':
            i+=2
            while i+1<n and not(src[i]=='*' and src[i+1]=='/'): i+=1
            i+=2; continue
        if c=='@' and nx=='"':
            i+=2
            while i<n:
                if src[i]=='"' and i+1<n and src[i+1]=='"': i+=2; continue
                if src[i]=='"': i+=1; break
                i+=1
            out.append('""'); continue
        if c=='"':
            i+=1
            while i<n and src[i]!='"': i+= 2 if src[i]=='\\' else 1
            i+=1; out.append('""'); continue
        if c=="'":
            i+=1
            while i<n and src[i]!="'": i+= 2 if src[i]=='\\' else 1
            i+=1; out.append("''"); continue
        out.append(c); i+=1
    return ''.join(out)
n=0
for d in ("Runtime","Editor","Tests"):
    for p in (pkg/d).rglob("*.cs"):
        n += len(re.findall(r'\b(?:class|struct|interface|enum|delegate)\s+\w+',
                            strip_cs(p.read_text(encoding='utf-8-sig'))))
print(n)
PYEOF
}

# ─────────────────────────────────────────────────────────────
head_ "1. 文件索引自检"
# ─────────────────────────────────────────────────────────────
if [ ! -f "$INDEX" ]; then
  bad "$INDEX 不存在"
else
  # 只统计参与编译的三个目录；Samples~ 带 ~ 尾缀，Unity 不编译
  actual_files=$(find "$PKG/Runtime" "$PKG/Editor" "$PKG/Tests" -name '*.cs' -type f 2>/dev/null | wc -l | tr -d ' ')
  actual_types=$(count_types)

  declared_files=$(grep -oE '\*\*[0-9]+\s*个\s*`?\.cs' "$INDEX" | head -1 | grep -oE '[0-9]+')
  declared_types=$(grep -oE '\*\*[0-9]+\s*个类型' "$INDEX" | head -1 | grep -oE '[0-9]+')

  if [ -z "$declared_files" ] || [ -z "$declared_types" ] || [ "$actual_types" = "count_types_error" ]; then
    bad "无法统计类型数（缺 python3？）或无法从 $INDEX 解析声明数，格式可能变了"
  else
    if [ "$actual_files" = "$declared_files" ]; then
      pass "文件数一致（$actual_files）"
    else
      bad "文件数不一致：声明 $declared_files，实际 $actual_files → 需更新 $INDEX 与 com.hyc.framework/Documentation~/README.md"
    fi
    if [ "$actual_types" = "$declared_types" ]; then
      pass "类型数一致（$actual_types）"
    else
      bad "类型数不一致：声明 $declared_types，实际 $actual_types → 需更新 $INDEX 与 com.hyc.framework/Documentation~/README.md"
    fi
  fi

  # 分组之和
  group_sum=$(grep -oE '^###\s+`[^`]+`（[0-9]+' "$INDEX" | grep -oE '[0-9]+' \
              | awk '{s+=$1} END {print s+0}')
  if [ -n "$declared_files" ] && [ "$group_sum" = "$declared_files" ]; then
    pass "分组标题之和等于总声明文件数（$group_sum）"
  else
    bad "分组标题之和为 $group_sum，与总声明 $declared_files 不一致（索引内部自相矛盾）"
  fi
fi

# ─────────────────────────────────────────────────────────────
head_ "2. 新增 .cs 的 .meta 完整性"
# ─────────────────────────────────────────────────────────────
missing_meta=0
while IFS= read -r cs; do
  if [ ! -f "$cs.meta" ]; then
    bad "缺少 .meta: $cs"
    missing_meta=$((missing_meta + 1))
  fi
done < <(find "$PKG/Runtime" "$PKG/Editor" "$PKG/Tests" -name '*.cs' -type f)
[ "$missing_meta" -eq 0 ] && pass "所有参与编译的 .cs 都有 .meta"

# .meta 孤儿（有 .meta 但 .cs 不见了）同样要报，否则 Unity 会持续报警
orphan_meta=0
while IFS= read -r meta; do
  cs="${meta%.meta}"
  if [ ! -f "$cs" ]; then
    bad "孤儿 .meta（源文件已删，记得连 .meta 一起删）: $meta"
    orphan_meta=$((orphan_meta + 1))
  fi
done < <(find "$PKG/Runtime" "$PKG/Editor" "$PKG/Tests" -name '*.cs.meta' -type f)
[ "$orphan_meta" -eq 0 ] && pass "没有孤儿 .meta"

# ─────────────────────────────────────────────────────────────
head_ "3. tag 与包版本一致性"
# ─────────────────────────────────────────────────────────────
if [ "$SKIP_TAG" -eq 1 ]; then
  printf '  [SKIP] 已指定 --skip-tag\n'
elif ! command -v git >/dev/null 2>&1; then
  printf '  [SKIP] 环境无 git\n'
else
  # 复用独立脚本的判定逻辑（含"历史遗留不判失败"基线）
  if bash scripts/verify-tag-version.sh; then
    pass "tag 与 package.json 版本一致"
  else
    bad "tag 与 package.json 版本不一致（详见上方输出）"
  fi
fi

# ─────────────────────────────────────────────────────────────
head_ "4. 菜单英文化约定"
# ─────────────────────────────────────────────────────────────
# 主菜单一律英文；Samples~ 下 3 个中文项是有意例外（面向中文使用者的示例入口）
cjk_hits=$(grep -rn 'MenuItem("[^"]*[一-龥]' "$PKG/Runtime" "$PKG/Editor" "$PKG/Tests" \
             --include='*.cs' 2>/dev/null | grep -v '/Samples~/' || true)
if [ -z "$cjk_hits" ]; then
  pass "包内主菜单路径全英文"
else
  bad "主菜单路径含中文："
  echo "$cjk_hits" | sed 's/^/         /'
fi

# ─────────────────────────────────────────────────────────────
head_ "5. 非泛型声明不得带 where 约束（CS0080）"
# ─────────────────────────────────────────────────────────────
# 血泪教训：仿照 BlobRoot<TRow> 给非泛型的 struct 加了 where 约束，
# 编译器直接 CS0080。沙箱禁 csc ⇒ 这类纯语法错必须静态抓，别等 Unity 报。
# 检查法：class/struct/interface NAME 之后没有 <...> 却跟了 where ⇒ 违规。
cs0080=$(python3 - "$PKG" <<'PY'
import os, re, sys
pkg = sys.argv[1]

def strip(src):
    out=[];i=0;n=len(src)
    while i<n:
        c=src[i]
        if c=='/' and i+1<n and src[i+1]=='/':
            while i<n and src[i]!='\n': i+=1
        elif c=='/' and i+1<n and src[i+1]=='*':
            i+=2
            while i+1<n and not(src[i]=='*' and src[i+1]=='/'): i+=1
            i+=2
        elif c=='@' and i+1<n and src[i+1]=='"':
            i+=2
            while i<n:
                if src[i]=='"' and i+1<n and src[i+1]=='"': i+=2; continue
                if src[i]=='"': i+=1; break
                i+=1
        elif c=='"':
            i+=1
            while i<n and src[i]!='"':
                if src[i]=='\\': i+=1
                i+=1
            i+=1
        elif c=="'":
            i+=1
            while i<n and src[i]!="'":
                if src[i]=='\\': i+=1
                i+=1
            i+=1
        else:
            out.append(c); i+=1
    return ''.join(out)

pat = re.compile(r'\b(?:class|struct|interface)\s+(\w+)\s*(<[^>{]*>)?\s*(where\s+\w+\s*:\s*[^\s{]+)\s*\{')
for root, dirs, files in os.walk(pkg):
    dirs[:] = [d for d in dirs if d != '.git']
    for f in files:
        if not f.endswith('.cs'): continue
        p = os.path.join(root, f).replace('\\','/')
        s = strip(open(p, encoding='utf-8-sig', errors='replace').read())
        for m in pat.finditer(s):
            if not m.group(2):
                print('%s:%s  非泛型 %s 带了 where 约束' % (p, m.group(1), m.group(1)))
PY
)
if [ -z "$cs0080" ]; then
  pass "无非泛型声明带 where 约束"
else
  bad "CS0080 风险："
  echo "$cs0080" | sed 's/^/         /'
fi

# ─────────────────────────────────────────────────────────────
head_ "6. InitializeOnLoad 必须修饰 class（CS0592）"
# ─────────────────────────────────────────────────────────────
# 血泪教训：代码生成器把 [UnityEditor.InitializeOnLoad] 吐到了静态构造器上，
# 而它的语义是"修饰类、由 Unity 在程序集重载后调用该类静态构造器"，
# 挂到构造器上 Unity 直接 CS0592。生成产物在宿主工程里、包内编译不到 ⇒ 只有静态抓。
# 注意区分 InitializeOnLoadMethod（合法的方法特性），用 \b 天然排除。
iol=$(python3 - "$PKG" <<'PY'
import os, re, sys
pkg = sys.argv[1]

def strip(src):
    out=[];i=0;n=len(src)
    while i<n:
        c=src[i]
        if c=='/' and i+1<n and src[i+1]=='/':
            while i<n and src[i]!='\n': i+=1
        elif c=='/' and i+1<n and src[i+1]=='*':
            i+=2
            while i+1<n and not(src[i]=='*' and src[i+1]=='/'): i+=1
            i+=2
        elif c=='@' and i+1<n and src[i+1]=='"':
            i+=2
            while i<n:
                if src[i]=='"' and i+1<n and src[i+1]=='"': i+=2; continue
                if src[i]=='"': i+=1; break
                i+=1
            out.append('""')
        elif c=='"':
            i+=1
            while i<n and src[i]!='"':
                if src[i]=='\\': i+=1
                i+=1
            i+=1; out.append('""')
        elif c=="'":
            i+=1
            while i<n and src[i]!="'":
                if src[i]=='\\': i+=1
                i+=1
            i+=1
        else:
            out.append(c); i+=1
    return ''.join(out)

attr = re.compile(r'^\s*\[\s*(?:[\w.]+\.)?InitializeOnLoad\b\s*\]')
inline = re.compile(r'^(?:public\s+|internal\s+|sealed\s+|abstract\s+|static\s+|partial\s+)*'
                    r'(?:class|struct|record)\b')
decl = re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:public\s+|internal\s+|private\s+|sealed\s+|abstract\s+|static\s+|partial\s+)*'
                  r'(?:class|struct|record|namespace|interface)\b')
for root, dirs, files in os.walk(pkg):
    dirs[:] = [d for d in dirs if d != '.git']
    for f in files:
        if not f.endswith('.cs'): continue
        p = os.path.join(root, f).replace('\\','/')
        lines = strip(open(p, encoding='utf-8-sig', errors='replace').read()).split('\n')
        for i, ln in enumerate(lines):
            m = attr.match(ln)
            if not m: continue
            if inline.match(ln[m.end():].strip()): continue   # 同行类声明，合规
            j = i + 1
            while j < len(lines):
                nxt = lines[j].strip()
                if not nxt or nxt.startswith('[') or nxt.startswith('//'):
                    j += 1; continue
                break
            if j < len(lines) and decl.match(lines[j]):
                continue                                       # 下一个声明是类型，合规
            tgt = lines[j].strip() if j < len(lines) else '<文件结束>'
            print('%s:%d  InitializeOnLoad 修饰的是非类型声明 → %s' % (p, i+1, tgt[:70]))
PY
)
if [ -z "$iol" ]; then
  pass "InitializeOnLoad 全部修饰在 class 上"
else
  bad "CS0592 风险："
  echo "$iol" | sed 's/^/         /'
fi

# ─────────────────────────────────────────────────────────────
printf '\n'
if [ "$fail" -eq 0 ]; then
  printf '\033[32m══ 全部通过 ══\033[0m\n'
else
  printf '\033[31m══ 有失败项 ══\033[0m\n'
fi
exit "$fail"
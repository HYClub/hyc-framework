#!/usr/bin/env bash
# 校验 git tag 与 package.json 的 version 是否一致。
#
# 为什么需要：中台化的第一道闸门。框架被使用方以 file: 方式引用、
# 没有版本锁，任何"顺手改个包"都无从察觉。而 UPM 的包版本是有语义的
# ——它决定 Package Manager 的依赖解析与缓存键。tag 与包版本脱节时，
# tag 就失去了"可寻址的发布点"这个作用。
#
# 已发现的历史问题（2026-10-07 实测）：
#   v2.0.0 / v2.0.1 / v2.0.2 / v2.0.3 / v2.0.4 六个 tag 内的 package.json
#   全部写着 "1.0.0"，即 tag 名与包版本完全脱节；且缺 v2.0.5（tag 跳号）。
#
# 用法：
#   bash scripts/verify-tag-version.sh              # 校验全部 tag + 工作区
#   bash scripts/verify-tag-version.sh --latest-only # 只校验最新 tag 与工作区
#   bash scripts/verify-tag-version.sh --fix-note    # 仅列出不一致项，不退出码 1
#
# 退出码：0 = 全部一致；1 = 存在不一致（CI 应据此失败）

set -u

PKG_PATH="com.hyc.framework/package.json"
LATEST_ONLY=0
for arg in "$@"; do
  case "$arg" in
    --latest-only) LATEST_ONLY=1 ;;
    --fix-note)    LATEST_ONLY=1 ;;   # 兼容：只报告
  esac
done

if [ ! -f "$PKG_PATH" ]; then
  echo "[错误] 找不到 $PKG_PATH —— 请在仓库根执行本脚本。"
  exit 1
fi

# 从 package.json 取 version。用 grep/sed 而非 jq：避免依赖 jq。
pkg_version() {
  grep -m1 '"version"' "$1" | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/'
}

CURRENT=$(pkg_version "$PKG_PATH")
if [ -z "$CURRENT" ]; then
  echo "[错误] 无法从 $PKG_PATH 解析 version。"
  exit 1
fi

# 历史 tag 基线：这些 tag 已发布，其 package.json 版本与 tag 名脱节是既成事实。
# 历史 tag 无法改写（改了会让已引用它的下游失效），因此脚本只**报告**不失败；
# 但基线之外的任何新 tag 若不一致，一律失败退出码 1。
#
# 新增 tag 时请同步更新此列表（并确认它确实只是历史遗留问题）。
KNOWN_LEGACY_MISMATCH="v2.0.0 v2.0.1 v2.0.2 v2.0.3 v2.0.4"

is_known_legacy() {
  for k in $KNOWN_LEGACY_MISMATCH; do
    [ "$1" = "$k" ] && return 0
  done
  return 1
}

fail=0
legacy=0
checked=0

echo "包当前版本：$CURRENT"
echo ""

if [ "$LATEST_ONLY" -eq 1 ]; then
  TAGS=$(git tag --sort=-v:refname | head -1)
else
  TAGS=$(git tag --sort=v:refname)
fi

if [ -z "$TAGS" ]; then
  echo "[警告] 仓库没有任何 tag。"
  exit 0
fi

for t in $TAGS; do
  # 逐个 tag 读该提交下的 package.json
  blob=$(git show "$t:$PKG_PATH" 2>/dev/null)
  if [ -z "$blob" ]; then
    echo "  [跳过] $t —— 该提交下无 $PKG_PATH"
    continue
  fi

  v=$(echo "$blob" | grep -m1 '"version"' | sed -E 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')
  if [ -z "$v" ]; then
    echo "  [跳过] $t —— 无法解析 version"
    continue
  fi

  checked=$((checked + 1))
  tag_ver=${t#v}   # 去掉前导 v

  if [ "$v" = "$tag_ver" ]; then
    echo "  [一致]     $t（$v）"
  elif is_known_legacy "$t"; then
    echo "  [历史遗留] $t → 包版本 $v（已列入 KNOWN_LEGACY_MISMATCH，不判失败）"
    legacy=$((legacy + 1))
  else
    echo "  [不一致]   $t → tag 名暗示 $tag_ver，但包版本是 $v"
    fail=1
  fi
done

echo ""

# 最新 tag 与工作区的偏离
latest=$(git tag --sort=-v:refname | head -1)
if [ -n "$latest" ]; then
  latest_ver=${latest#v}
  if [ "$latest_ver" != "$CURRENT" ]; then
    echo "[提示] 工作区版本（$CURRENT）高于最新 tag（$latest_ver）。"
    echo "       若本次改动已可发布，考虑补打 tag：git tag -a v$CURRENT -m \"...\""
    echo "       这不是错误，只是提醒。"
  fi
fi

echo ""
if [ "$fail" -eq 0 ]; then
  echo "结果：$checked 个 tag 检查完毕，$legacy 个为已知历史遗留，0 个新问题 ✓"
  echo "（历史遗留见 KNOWN_LEGACY_MISMATCH；新 tag 若不一致会失败退出码 1）"
else
  echo "结果：存在需处理的新问题 ✗"
  echo ""
  echo "处理方式："
  echo "  1. 若该 tag 尚未发布 —— 删掉它（git tag -d <tag>），改对package.json 后重新打；"
  echo "  2. 若该 tag 已发布 —— 无法改写（会破坏下游引用），请确认它是否只是历史遗留；"
  echo "     若是，在脚本的 KNOWN_LEGACY_MISMATCH 中登记并附一句说明，"
  echo "     同时在 CHANGELOG 写勘误。**不要为了让 CI 变绿而随便加进基线**。"
fi

exit $fail
#!/usr/bin/env bash
#
# 冒烟测试：验证「文档导入 → 问答 → 反馈」主链路是否仍然可用。
#
# 定位：改动代码后一条命令确认没有把已实现的功能改坏。
#       它只做端到端存活检查，不做逻辑校验，不替代单元测试。
#       当前项目阶段（仅解析层落地）用它守住已有的可用行为即可。
#
# 用法：
#   scripts/smoke.sh <手册PDF> [宣传册PDF]
#   SMOKE_MANUAL_PDF=/path/a.pdf SMOKE_BROCHURE_PDF=/path/b.pdf scripts/smoke.sh
#
# 环境变量：
#   SMOKE_BASE_URL   服务地址，默认 http://localhost:5286
#   SMOKE_NO_START   设为 1 时不自动拉起服务（假定已在运行）
#
# 退出码：0 = 全部通过；1 = 有检查项失败

set -uo pipefail

BASE_URL="${SMOKE_BASE_URL:-http://localhost:5286}"
MANUAL_PDF="${1:-${SMOKE_MANUAL_PDF:-}}"
BROCHURE_PDF="${2:-${SMOKE_BROCHURE_PDF:-}}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PROJECT_REL="src/PdfRagQa.Api/PdfRagQa.Api.csproj"
API_DIR_REL="src/PdfRagQa.Api"
# 相对 Api 项目目录的 DLL 路径（运行时以该目录为工作目录，才能读到 appsettings.json）
API_DLL_REL="bin/Debug/net10.0/PdfRagQa.Api.dll"

TMP_DIR="$(mktemp -d)"
# curl 是原生 Windows 程序，读不了 MSYS 的 /tmp 路径，需转成 C:/... 形式
TMP_DIR_WIN="$(cygpath -m "$TMP_DIR" 2>/dev/null || printf '%s' "$TMP_DIR")"
API_PID=""
FAILED=0
STATUS=""
BODY=""

cleanup() {
  if [[ -n "$API_PID" ]]; then
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT

pass() { printf '  [PASS] %s\n' "$1"; }
fail() { printf '  [FAIL] %s\n' "$1"; FAILED=1; }
info() { printf '  [SKIP] %s\n' "$1"; }

# JSON 里统一用正斜杠：既避开 Windows 反斜杠转义，.NET 在 Windows 上也同样接受
to_slashes() { printf '%s' "${1//\\//}"; }

api_reachable() {
  local code
  code=$(curl -s -o /dev/null -m 3 -w '%{http_code}' -X POST "$BASE_URL/api/questions" \
    -H 'Content-Type: application/json' -d '{}' 2>/dev/null)
  [[ -n "$code" && "$code" != "000" ]]
}

# 发出请求，结果写入全局 STATUS / BODY。
# 请求体一律走文件：Git Bash 会把非 ASCII 的命令行参数转成系统 ANSI 编码再交给原生 curl，
# 直接内联 -d 会把中文发成非 UTF-8 字节，服务端反序列化直接 400。
run_post() {
  local endpoint="$1" body_file="$2" raw
  raw=$(curl -s -m 300 -X POST "$BASE_URL$endpoint" \
    -H 'Content-Type: application/json; charset=utf-8' \
    --data-binary "@$body_file" -w $'\n%{http_code}')
  STATUS=$(printf '%s' "$raw" | tail -n1)
  BODY=$(printf '%s' "$raw" | sed '$d')
}

echo "冒烟测试 → $BASE_URL"
echo

# ---------- 0. 服务就绪 ----------
if api_reachable; then
  pass "服务已在运行"
elif [[ "${SMOKE_NO_START:-0}" == "1" ]]; then
  fail "服务不可达（SMOKE_NO_START=1，请先手动启动）"
  exit 1
else
  printf '  服务未运行，正在构建并启动...\n'
  # 注意：必须用相对路径调用 dotnet，Git Bash 的 /d/... 形式会被 MSBuild 当成命令行开关
  if ! ( cd "$REPO_ROOT" && dotnet build "$API_PROJECT_REL" --nologo --verbosity quiet ) \
      >"$TMP_DIR/build.log" 2>&1; then
    fail "构建失败"
    tail -n 20 "$TMP_DIR/build.log"
    exit 1
  fi

  ( cd "$REPO_ROOT/$API_DIR_REL" && exec dotnet "$API_DLL_REL" --urls "$BASE_URL" ) \
    >"$TMP_DIR/api.log" 2>&1 &
  API_PID=$!

  for _ in $(seq 1 40); do
    if api_reachable; then break; fi
    sleep 1
  done

  if api_reachable; then
    pass "服务已启动"
  else
    fail "服务启动超时"
    tail -n 20 "$TMP_DIR/api.log"
    exit 1
  fi
fi

# ---------- 1. 手册导入（文本链路）----------
echo
echo "1) 手册导入（文本链路）"
if [[ -z "$MANUAL_PDF" ]]; then
  info "未提供手册样例，跳过。用法：scripts/smoke.sh <手册PDF>"
elif [[ ! -f "$MANUAL_PDF" ]]; then
  fail "样例文件不存在：$MANUAL_PDF"
else
  printf '{"filePath":"%s","documentType":0,"version":"smoke"}' "$(to_slashes "$MANUAL_PDF")" \
    >"$TMP_DIR/manual.json"
  run_post /api/documents/import "$TMP_DIR_WIN/manual.json"
  if [[ "$STATUS" != "200" ]]; then
    fail "HTTP $STATUS：$(printf '%s' "$BODY" | head -c 300)"
  elif ! printf '%s' "$BODY" | grep -q '"chunkCount":[1-9]'; then
    fail "未写入任何 chunk：$(printf '%s' "$BODY" | head -c 300)"
  else
    pass "HTTP 200，$(printf '%s' "$BODY" | grep -o '"chunkCount":[0-9]*')"
  fi
fi

# ---------- 2. 宣传册导入（视觉链路）----------
echo
echo "2) 宣传册导入（渲染 + 视觉链路）"
if [[ -z "$BROCHURE_PDF" ]]; then
  info "未提供宣传册样例，跳过"
elif [[ ! -f "$BROCHURE_PDF" ]]; then
  fail "样例文件不存在：$BROCHURE_PDF"
else
  printf '{"filePath":"%s","documentType":1,"version":"smoke"}' "$(to_slashes "$BROCHURE_PDF")" \
    >"$TMP_DIR/brochure.json"
  run_post /api/documents/import "$TMP_DIR_WIN/brochure.json"
  if [[ "$STATUS" != "200" ]]; then
    fail "HTTP $STATUS：$(printf '%s' "$BODY" | head -c 300)"
  else
    # 未配置视觉模型时 chunkCount 为 0 属预期，此处只要求链路不报错
    pass "HTTP 200，$(printf '%s' "$BODY" | grep -o '"chunkCount":[0-9]*')"
  fi
fi

# ---------- 3. 问答 ----------
echo
echo "3) 问答接口"
printf '{"question":"接线"}' >"$TMP_DIR/question.json"
run_post /api/questions "$TMP_DIR_WIN/question.json"
if [[ "$STATUS" != "200" ]]; then
  fail "HTTP $STATUS：$(printf '%s' "$BODY" | head -c 300)"
elif ! printf '%s' "$BODY" | grep -q '"answer"'; then
  fail "响应缺少 answer 字段"
elif ! printf '%s' "$BODY" | grep -q '"citations":\[{'; then
  # 中文分词或 BM25 计分一旦失效，检索会返回空集——这条断言专门守它
  fail "检索未返回任何引用（citations 为空）：$(printf '%s' "$BODY" | head -c 300)"
else
  pass "HTTP 200，返回 answer 与 citations"
fi

# ---------- 4. 反馈 ----------
echo
echo "4) 反馈接口"
printf '{"conversationId":"smoke","question":"smoke","answer":"smoke","isUpvote":true}' >"$TMP_DIR/feedback.json"
run_post /api/feedback "$TMP_DIR_WIN/feedback.json"
if [[ "$STATUS" != "200" ]]; then
  fail "HTTP $STATUS：$(printf '%s' "$BODY" | head -c 300)"
else
  pass "HTTP 200"
fi

# ---------- 汇总 ----------
echo
if [[ "$FAILED" == "0" ]]; then
  echo "冒烟测试通过"
  exit 0
else
  echo "冒烟测试存在失败项"
  exit 1
fi

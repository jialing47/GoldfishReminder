#!/bin/bash
# 每日打 GoldfishReminder 的 daily reminder job
set -euo pipefail

# 載入 JOBS_TOKEN
source /etc/goldfish-cron.env
# 防呆 去掉 token 可能夾帶的 CR 與前後空白 避免 env 存成 CRLF 造成 401
JOBS_TOKEN="$(printf '%s' "$JOBS_TOKEN" | tr -d '\r' | xargs)"

# 打 endpoint 並把結果與時戳寫進 log
{
  echo "--- $(date '+%Y-%m-%d %H:%M:%S %z') ---"
  curl -sS -X POST \
    -H "Authorization: Bearer $JOBS_TOKEN" \
    -w "HTTP %{http_code} in %{time_total}s\n" \
    https://<DOMAIN>/api/jobs/daily-reminder
  echo ""
} >> /var/log/goldfish-daily.log 2>&1

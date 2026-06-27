# 部署（deploy/）

GoldfishReminder 的部署資產與操作手冊。架構與設計取捨見專案根目錄 `README.md`；本檔聚焦「**怎麼部署**」。

## 結構

```
publish.bat                  本機發佈打包：dotnet publish（self-contained + single-file）→ goldfish-app.zip（在 repo 根）
deploy/                      VM 上的設定與腳本快照（可直接還原回 VM）
  goldfish.service           systemd unit        → /etc/systemd/system/goldfish.service
  nginx-default.conf         nginx site          → /etc/nginx/sites-enabled/default（443 SSL 反代 localhost:5000，certbot 管理）
  crontab.root               root crontab        → sudo crontab -（daily reminder + keep-warm）
  goldfish-daily.sh          daily reminder 觸發  → /usr/local/bin/goldfish-daily.sh（從 /etc/goldfish-cron.env 取 token）
  goldfish-deploy            VM 端部署腳本        → /usr/local/bin/goldfish-deploy（停服務/備份/保護 config/解壓/啟動/健康檢查/失敗 rollback）
  goldfish-cron.env.example  /etc/goldfish-cron.env 範本（真檔含 token，不進版控）
```

## Placeholder（還原前替換）

設定快照已把個資／部署值換成 placeholder，**還原回 VM 前先替換**：

| placeholder | 實際值 |
|---|---|
| `<DOMAIN>` | 你的網域（如 DuckDNS subdomain） |
| `<VM_USER>` | VM 使用者名 |

一鍵替換（在 `deploy/` 下執行）：

    grep -rl '<DOMAIN>\|<VM_USER>' . | xargs sed -i 's/<DOMAIN>/你的網域/g; s/<VM_USER>/你的VM帳號/g'

## 例行部署（已上線後出新版，最常用）

1. **本機**：執行 repo 根的 `publish.bat` → 產出 `goldfish-app.zip`
2. **傳上 VM**：把 `goldfish-app.zip` 傳到 VM 的 `~/goldfish-app.zip`（scp 或你慣用方式）
3. **VM 上**：`goldfish-deploy ~/goldfish-app.zip`
   - 自動：停服務 → 備份舊版到 `~/goldfish-app.bak` → 暫存並還原 `appsettings.Production.json`(chmod 600) → 解壓 → 啟動 → 健康檢查（localhost:5000 / 對外 URL / logo.png）→ **失敗自動 rollback**
4. **驗證**：`sudo journalctl -u goldfish -f`

> `publish.bat` 發佈為 self-contained + single-file，對應 `goldfish.service` 的 `ExecStart=.../GoldfishReminder.Api`（單一可執行檔）。若改成 framework-dependent 散檔，服務會 `Failed with result 'exit-code'`。

## 首次 / 重建 VM

VM 規格、swap、時區等見根 `README.md` 的「部署」段。把本資料夾的設定套上去：

```bash
# ── 系統前置 ──
# 套件（goldfish-deploy 用 unzip；nginx/certbot 走 HTTPS）
sudo apt update && sudo apt install -y nginx certbot python3-certbot-nginx unzip

# 時區（crontab 時間以台灣時區計）
sudo timedatectl set-timezone Asia/Taipei

# 2GB swap 防 OOM（e2-micro 只有 1GB RAM）
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

# ── DNS / 防火牆（要在 certbot 之前：憑證申請需 <DOMAIN> 已指向本機且 80/443 可達）──
# DuckDNS：到 duckdns.org 註冊 subdomain，IP 指向 VM 外部 IP；建議設動態更新 cron：
#   */5 * * * * curl -s "https://www.duckdns.org/update?domains=<subdomain>&token=<duckdns-token>&ip=" >/dev/null
# GCP 防火牆：開放 80 / 443（Console 勾「允許 HTTP/HTTPS 流量」或 gcloud compute firewall-rules）

# ── 資料庫（全新 Neon 要先建 schema；可在任何能連 Neon 的機器跑）──
psql "<Neon 連線字串>" -f schema.sql

# ── 應用部署 ──
# 1. 應用目錄與正式設定（含 secret，手動建立，不進版控）
mkdir -p ~/goldfish-app
nano ~/goldfish-app/appsettings.Production.json   # 填 Discord/連線字串/Jobs:AuthToken 等
chmod 600 ~/goldfish-app/appsettings.Production.json

# 2. systemd
sudo cp deploy/goldfish.service /etc/systemd/system/goldfish.service
sudo systemctl daemon-reload && sudo systemctl enable goldfish

# 3. nginx + SSL（憑證由 certbot 管理）
sudo cp deploy/nginx-default.conf /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d <DOMAIN>             # 首次申請憑證
sudo systemctl status certbot.timer          # 確認自動續期 timer 啟用（避免 90 天後靜默過期）
sudo certbot renew --dry-run                 # 測試續期流程能跑通

# 4. daily reminder 腳本 + 它的 token env（env 含 secret，照範本手動建）
sudo cp deploy/goldfish-daily.sh /usr/local/bin/ && sudo chmod +x /usr/local/bin/goldfish-daily.sh
sudo cp deploy/goldfish-cron.env.example /etc/goldfish-cron.env
sudo nano /etc/goldfish-cron.env   # 填真實 JOBS_TOKEN（= Jobs:AuthToken）
sudo chmod 600 /etc/goldfish-cron.env

# 5. 部署腳本 + cron
sudo cp deploy/goldfish-deploy /usr/local/bin/ && sudo chmod +x /usr/local/bin/goldfish-deploy
sudo crontab - < deploy/crontab.root

# 6. 首次部署
goldfish-deploy ~/goldfish-app.zip
```

## 不進版控（含 secret）

| 項目 | 位置 | 文件化替代 |
|---|---|---|
| Jobs token | `/etc/goldfish-cron.env` | `deploy/goldfish-cron.env.example` |
| 正式設定（Bot token / 連線字串 / public key） | `~/goldfish-app/appsettings.Production.json`（chmod 600） | — |
| 建置產物 | `publish/`、`goldfish-app.zip` | — |

## 回滾

`goldfish-deploy` 啟動失敗會自動 rollback。手動：

```bash
sudo systemctl stop goldfish && rm -rf ~/goldfish-app && mv ~/goldfish-app.bak ~/goldfish-app && sudo systemctl start goldfish
```

## 排錯

| 症狀 | 排查 |
|---|---|
| 服務起不來 `exit-code` | 確認發佈是 self-contained single-file（`ExecStart` 指單一執行檔 `GoldfishReminder.Api`）；`sudo journalctl -u goldfish -n 50 --no-pager` |
| daily reminder 沒跑 | `cat /var/log/goldfish-daily.log`；檢查 `/etc/goldfish-cron.env` 的 `JOBS_TOKEN` |
| daily job 回 401 | token 夾帶 CR/空白（`goldfish-daily.sh` 已用 `tr -d '\r'` 防呆，確認 env 值正確） |
| 對外 URL 沒回應 | nginx / SSL：`sudo nginx -t`、`sudo systemctl status nginx`、憑證 `sudo certbot certificates` |

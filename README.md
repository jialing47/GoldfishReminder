# 金魚腦助手 GoldfishReminder

Discord bot 搭配網頁後台 幫你記住每張信用卡的結帳日 繳費日 扣款帳戶餘額 時間到自動提醒。

## 功能

- **自動提醒** 每日排程掃所有信用卡 該結帳 該繳費 該確認餘額的時候送 Discord 私訊
- **網頁管理** 新增修改帳戶 信用卡 本月帳單金額 查歷史帳單
- **Discord 快速指令** `/balance` 不用開網頁直接更新帳戶餘額

## 技術棧

- ASP.NET Core 10 (Razor Pages + Controllers)
- Entity Framework Core + PostgreSQL
- Discord HTTP Interactions (非 Gateway 連線 純 webhook)
- 部署：GCP Compute Engine e2-micro VM + Neon PostgreSQL + DuckDNS + nginx + Let's Encrypt

## 專案結構

```
GoldfishReminder.Api            Web / Controller / Razor 進入點
  Controllers/                  Discord interaction 與 job 觸發
  Pages/                        網頁設定介面
  Background/                   背景工作佇列
  Security/                     Discord 簽章驗證
  Jobs/                         每日提醒 job

GoldfishReminder.Application    核心邏輯
  Workflows/                    CreditBillWorkflow / NotificationWorkflow / MessageBuilder
  Services/                     介面定義
  Models/                       DTO 與 workflow context
  TaiwanClock.cs                台灣時區工具 全專案共用

GoldfishReminder.Domain         Entity 定義

GoldfishReminder.Infrastructure EF Core / 外部 API / 服務實作
  Persistence/                  AppDbContext
  Services/                     實作類別
  Configuration/                Options 配置
```

## 核心流程

### 第一次使用 Discord → 網頁綁定

1. User 在公告頻道點「取得網頁連結」按鈕 觸發 `gr_link` interaction
2. Controller 立刻回 deferred response (type 5) 工作丟背景處理
3. 背景 worker 查 User 表 沒有就新增 建立 Discord 私人頻道
4. 產生一次性 `WebLinkToken` 經 followup 訊息發到私人頻道
5. User 點連結進網頁 驗證 token 開始使用

### 每日提醒 Job

走 `POST /api/jobs/daily-reminder` 觸發 實際工作丟 `TaskQueueHostedService` 非同步執行 須帶 `Authorization: Bearer <Jobs:AuthToken>`。

1. 補建帳單 今日是結帳日的 `CreditSetting` 自動建當月的 `CreditBill`
2. 掃所有未繳 `CreditBill` 依狀態決策動作
   - 需要輸入金額 送「請輸入本期帳單金額」按鈕
   - 金額已確認 餘額足 且今日達繳費日 自動扣款 標記 Paid
   - 金額已確認 餘額不足 送「帳戶餘額不足」訊息
   - 無扣款帳戶 或 帳戶停用 送「請自行繳費」按鈕
   - 過繳費日 14 天 停用該 user 所有信用卡設定
3. 同一 user 同一天同一 target 同類型通知只送一次（去重）
4. 順便清 30 天前已過期或已使用的 web link token

### Discord `/balance` 指令

1. User 輸入 `/balance` autocomplete 列出該 user 的啟用帳戶
2. 選完帳戶跳 modal 輸入新餘額
3. 後端驗證帳戶擁有權 (`UserId` 比對) 更新餘額 呼叫 `ProcessAccountAsync` 重跑該帳戶底下所有信用卡的決策
4. 回覆 ephemeral「已更新 XXX 餘額為 YYY」

## 開發環境設定

### 需求

- .NET 10 SDK
- PostgreSQL（本地測試用）
- Discord application (要 bot 並開啟 `applications.commands` scope)
- 對外可連的 HTTPS 網址（本地開發用 ngrok 或 cloudflare tunnel）

### 設定步驟

**1. 複製 repo 建 DB**

```bash
git clone <repo>
cd GoldfishReminder

createdb goldfish
psql -d goldfish -f schema.sql
```

**2. 設定 User Secrets**

```bash
cd GoldfishReminder.Api
dotnet user-secrets set "Discord:BotToken" "你的 bot token"
dotnet user-secrets set "Discord:PublicKey" "Discord application 的 public key"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=goldfish;Username=postgres;Password=..."
dotnet user-secrets set "Jobs:AuthToken" "$(openssl rand -base64 32)"
dotnet user-secrets set "Web:BaseUrl" "https://你的-ngrok-或正式網址"
```

**3. 註冊 Discord `/balance` slash command**

打一次 Discord API 註冊 guild command 用 REST Client 或 curl。

```http
POST https://discord.com/api/v10/applications/{applicationId}/guilds/{guildId}/commands
Authorization: Bot {botToken}
Content-Type: application/json

{
  "name": "balance",
  "description": "更新銀行帳戶餘額",
  "type": 1,
  "options": [
    {
      "name": "account",
      "description": "選擇要更新的帳戶",
      "type": 3,
      "required": true,
      "autocomplete": true
    }
  ]
}
```

**4. 設定 Discord Interactions Endpoint**

Discord Developer Portal → General Information → Interactions Endpoint URL 填 `https://你的網址/api/discord/interactions` Discord 會送 ping 驗證能通才能存。

**5. 建公告訊息**

在 guild 挑個頻道 讓 bot 用 REST 發公告訊息 內容與按鈕範例：

```http
POST https://discord.com/api/v10/channels/{channelId}/messages
Authorization: Bot {botToken}
Content-Type: application/json

{
  "content": "<你的公告文字 markdown 可用>",
  "components": [
    {
      "type": 1,
      "components": [
        {
          "type": 2,
          "style": 1,
          "label": "取得網頁連結",
          "custom_id": "gr_link"
        }
      ]
    }
  ]
}
```

**6. 啟動**

```bash
dotnet run
```

## 正式部署架構

```
User → Discord → DuckDNS DNS 解析
                      ↓
            GCP e2-micro VM (us-central1)
              ├─ nginx (443 SSL terminate)
              │     ↓ proxy to localhost:5000
              └─ ASP.NET app (systemd 管理)
                      ↓ TCP+SSL (公網)
                Neon PostgreSQL (AWS us-west-2)
```

### 服務分工

| 元件 | 服務商 | 用途 | 費用 |
|---|---|---|---|
| App 主機 | GCP Compute Engine e2-micro | 跑 .NET app + nginx | $0（free tier） |
| 資料庫 | Neon | PostgreSQL 託管 | $0（free tier 0.5GB） |
| Domain | DuckDNS | 動態 DNS 提供 subdomain | $0 |
| SSL | Let's Encrypt + certbot | HTTPS 憑證自動續期 | $0 |
| 排程 | Linux cron | 每日觸發 daily reminder | $0 |

### VM 配置

- **規格**：e2-micro（2 vCPU shared, 1GB RAM, 30GB 標準磁碟）
- **OS**：Ubuntu 22.04 LTS Minimal
- **Region**：us-central1（GCP free tier 限定 us-central1 / us-west1 / us-east1）
- **Swap**：2GB（防 .NET app + nginx 同跑時瞬間 OOM）
- **時區**：Asia/Taipei

### 重要檔案位置

| 用途 | 路徑 |
|---|---|
| App 執行檔 | `~/goldfish-app/` |
| App 設定（含 secret） | `~/goldfish-app/appsettings.Production.json`（chmod 600） |
| systemd service | `/etc/systemd/system/goldfish.service` |
| nginx 設定 | `/etc/nginx/sites-enabled/goldfish` |
| SSL 憑證 | `/etc/letsencrypt/live/<duckdns-name>/` |
| App log | `sudo journalctl -u goldfish` |

### Cron 設定

VM 上 `crontab -e` 兩行：

```
# 每日台灣時間 10:00 觸發 daily reminder
0 2 * * * curl -s -X POST -H "Authorization: Bearer <token>" https://<你的網址>/api/jobs/daily-reminder > /dev/null 2>&1

# 每 5 分鐘 keep-warm 防 .NET app 被 swap 換出造成 cold start
*/5 * * * * curl -s -o /dev/null https://<你的網址>/api/discord/interactions -X POST -H "Content-Type: application/json" -d '{"type":1}'
```

`0 2 * * *` 是 UTC 02:00 = 台灣 10:00（VM 時區設 Asia/Taipei 也可寫 `0 10 * * *`）。

### 部署新版本流程

**本機 publish**：
```bash
cd GoldfishReminder.Api
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish
```

**上傳 + 重啟**：
```bash
gcloud compute scp --recurse ./publish/* goldfish-vm:~/goldfish-app/ --zone=us-central1-a
gcloud compute ssh goldfish-vm --zone=us-central1-a --command "sudo systemctl restart goldfish"
```

### DB 備份建議

VM 上設 cron 每天 `pg_dump` 把 Neon DB dump 一份留 7 天：

```bash
0 3 * * * pg_dump "$NEON_CONNECTION_STRING" | gzip > /home/ubuntu/backups/goldfish-$(date +\%Y\%m\%d).sql.gz && find /home/ubuntu/backups -name "goldfish-*.sql.gz" -mtime +7 -delete
```

Neon 免費版本身有自動 backup（7 天 PITR），這個是雙保險。
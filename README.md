# 金魚腦助手 GoldfishReminder

信用卡帳單提醒系統。每日排程掃描信用卡，在結帳日、繳費日、餘額不足時透過 Discord 推播，並提供網頁介面管理帳戶、信用卡、帳單金額與查詢歷史。

## 功能

- 自動提醒：每日排程掃所有信用卡，該結帳、該繳費、該確認餘額時送 Discord 私訊
- 網頁管理：新增修改帳戶、信用卡、本月帳單金額，查歷史帳單
- Discord 快速指令：`/balance` 不用開網頁直接更新帳戶餘額

## 設計取捨

實作上的幾個非預設選擇：

- **跨月信用卡的繳費日計算抽成 `CreditBillSchedule` 純函數**：結帳日號 > 繳費日號的卡（例如結帳 25 號、繳費 5 號）帳單需歸到下月繳款；過去散在 service / workflow 多處計算容易不一致，抽純函數後配上 `Math.Min(day, daysInMonth)` 處理「繳費日 31 號但 2 月只有 28 天」邊角，所有 caller 走同一條路徑

- **Daily reminder 一次撈完整 batch context 後純 in-memory 處理**：跨 region 到 Neon 的單次 round trip 約 100–150ms；改用一個 `GetDailyContextAsync` 撈完 users / bills / settings / accounts / 當日已送通知 keys，後續 N 筆 bill 全在 in-memory dictionary / HashSet 查表處理，把 DB round trip 從 O(N) 壓到 O(1)

- **Workflow method 簽章強制接 userId 做為 ownership 防線**：所有寫入路徑（例如 `MarkBillPaidAsync(billId, userId)`）method 簽章本身要求 userId 參數，內部直接 `if (entity.UserId != userId) throw UnauthorizedAccessException`；caller 從 signed cookie 或 Discord 簽章驗證過的 payload 取 userId 後傳入，編譯期就強制 IDOR check 不會漏寫

- **DB 層 partial unique index 兜底業務規則**：「每個 user 同時只能有一個有效 web link token」是業務規則，C# 的 `CreateOrRotateAsync` 邏輯把舊 token 標 used 後建新 token，但並發呼叫仍有 race；用 PostgreSQL partial unique index `WHERE used_at IS NULL` 在 DB 層兜底，並發插入會被 DB constraint 拒，程式碼簡潔且安全

## 技術棧

- ASP.NET Core 10（Razor Pages + Controllers）
- Entity Framework Core + PostgreSQL
- Discord HTTP Interactions（webhook 非 Gateway），Ed25519 簽章驗證
- 部署：GCP Compute Engine e2-micro + Neon PostgreSQL + DuckDNS + nginx + Let's Encrypt + Linux cron

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
  Entities/CreditBillSchedule   結帳日 / 繳費日 純函數計算

GoldfishReminder.Infrastructure EF Core / 外部 API / 服務實作
  Persistence/                  AppDbContext
  Services/                     實作類別
  Configuration/                Options 配置
```

## 系統架構

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

| 元件 | 服務商 | 用途 |
|---|---|---|
| App 主機 | GCP Compute Engine e2-micro | 跑 .NET app + nginx |
| 資料庫 | Neon | PostgreSQL 託管 |
| Domain | DuckDNS | 動態 DNS 提供 subdomain |
| SSL | Let's Encrypt + certbot | HTTPS 憑證自動續期 |
| 排程 | Linux cron | 每日觸發 daily reminder + keep-warm |

## 核心流程

### 第一次使用 Discord → 網頁綁定

1. User 在公告頻道點「取得網頁連結」按鈕，觸發 `gr_link` interaction
2. Controller 立刻回 deferred response (type 5)，工作丟背景處理
3. 背景 worker 查 User 表，沒有就新增。若 user 已有頻道紀錄則信任 DB 跳過 Discord API 驗證；否則在 `Discord:CategoryName` 底下建私人頻道 `gr-<discord-user-id>`，category 不存在會自動建
4. 產生一次性 `WebLinkToken` 送到私人頻道。若送訊息回 404 視為頻道被刪，自動重建頻道後重送
5. User 點連結進網頁，驗證 token 開始使用

### 每日提醒 Job

走 `POST /api/jobs/daily-reminder` 觸發，實際工作丟 `TaskQueueHostedService` 非同步執行，須帶 `Authorization: Bearer <Jobs:AuthToken>`。

1. 補建帳單：今日是結帳日的 `CreditSetting` 自動建當月的 `CreditBill`
2. 掃所有未繳 `CreditBill`，依狀態決策動作
   - 需要輸入金額：送「請輸入本期帳單金額」按鈕
   - 金額已確認、餘額足、且今日達繳費日：自動扣款，標記 Paid
   - 金額已確認、餘額不足：送「帳戶餘額不足」訊息
   - 無扣款帳戶、或帳戶停用：送「請自行繳費」按鈕
   - 過繳費日 7 天：停用該 user 所有信用卡設定
3. 同一 user 同一天同一 target 同類型通知只送一次。Job 開頭一次 SQL 撈出當日所有已送通知，後續逐筆判斷走 in-memory cache，不再打 DB
4. 順便清過期資料：30 天前已過期或已使用的 web link token，90 天前的通知紀錄

### Discord `/balance` 指令

走 deferred + 背景佇列，與 `gr_link` 同一套，避開 Discord 首次回應 3 秒硬限制。

1. User 輸入 `/balance`（無參數），Controller 立刻回 deferred response (type 5, ephemeral)
2. 背景 worker 查該 user 的啟用帳戶，用 followup 送出帶帳戶下拉選單（String Select Menu，最多 25 個）的 ephemeral 訊息
3. User 從下拉選單選帳戶，立即跳 modal 輸入新餘額（此步零 DB，modal 不能 defer）
4. 送出 modal 先驗金額格式，再回 deferred；背景驗證帳戶擁有權（`UserId` 比對）、更新餘額，呼叫 `ProcessAccountAsync` 重跑該帳戶底下所有信用卡的決策
5. followup 回覆 ephemeral「已更新 XXX 餘額為 YYY」

### 本月待繳 與 歷史帳單

兩個 tab 各自獨立的篩選與狀態。

**本月待繳**（動作導向）

- 篩選：未付款且未逾期
- 狀態：未設金額（紅）/ 需自行繳費（金）/ 待自動扣款（琥珀）

**歷史帳單**（結果導向）

- 篩選：使用者選的年月
- 狀態：已繳（綠）/ 未繳（琥珀）/ 逾期（紅）
- 今天到期當天還能繳，算「未繳」不算「逾期」

跨月信用卡（結帳日號 > 繳費日號，例如結帳 25 號、繳費 5 號）的帳單會歸到下月繳款。

## 架構選型

**Discord HTTP Interactions 而非 Gateway**：webhook 無狀態、不需維持長連線，對 e2-micro 友善。Gateway 要持續心跳，部署也要處理 reconnect 與 shard。

**Neon 託管 PG 非自架**：免費 0.5GB 對個人專案夠，自帶 7 天 PITR backup。自架 PG 在 1GB RAM 機器上會跟 .NET app 搶資源。

**Razor Pages 非 SPA**：設定中心互動度低，server-rendered + 少量 vanilla JS 已足夠。

**內建 channel queue 非 Hangfire / Quartz**：排程需求只有一個每日 job + 偶發 onboarding，`IBackgroundTaskQueue` + `BackgroundService` 已涵蓋。

**不串接銀行 API**：銀行 open API 在台灣覆蓋不全，且要走主動授權。改為由 user 手動輸入帳單金額，系統負責記住與提醒，資料完全留在 user 自架的 DB 與自己的 Discord 私人頻道。

## 開發環境設定

### 需求

- .NET 10 SDK
- PostgreSQL（本地測試用）
- Discord application（要 bot 並開啟 `applications.commands` scope）
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
dotnet user-secrets set "Discord:GuildId" "你的 Discord server (guild) ID"
dotnet user-secrets set "Discord:ApplicationId" "Discord application ID"
# Discord:CategoryName 可選 預設 GoldfishReminder bot 會在這個 category 下建私人提醒頻道
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=goldfish;Username=postgres;Password=..."
dotnet user-secrets set "Jobs:AuthToken" "$(openssl rand -base64 32)"
dotnet user-secrets set "Web:BaseUrl" "https://你的-ngrok-或正式網址"
```

**3. 註冊 Discord `/balance` slash command**

打一次 Discord API 註冊 guild command，用 REST Client 或 curl。此指令**無參數**，帳戶改由互動中的下拉選單（String Select Menu）選擇，不要帶 `account` option（舊版的 autocomplete option 已移除，若仍照舊註冊會讓 `/balance` 無法使用）。

```http
POST https://discord.com/api/v10/applications/{applicationId}/guilds/{guildId}/commands
Authorization: Bot {botToken}
Content-Type: application/json

{
  "name": "balance",
  "description": "更新銀行帳戶餘額",
  "type": 1
}
```

**4. 設定 Discord Interactions Endpoint**

Discord Developer Portal → General Information → Interactions Endpoint URL 填 `https://你的網址/api/discord/interactions`，Discord 會送 ping 驗證能通才能存。

**5. 建公告訊息**

在 guild 挑個頻道，讓 bot 用 REST 發公告訊息，內容與按鈕範例：

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

## 部署

### VM 配置

- 規格：e2-micro（2 vCPU shared, 1GB RAM, 30GB 標準磁碟）
- OS：Ubuntu 22.04 LTS Minimal
- Region：us-central1（GCP free tier 限定 us-central1 / us-west1 / us-east1）
- Swap：2GB（防 .NET app + nginx 同跑時瞬間 OOM）
- 時區：Asia/Taipei

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

```
# 每日台灣時間 10:00 觸發 daily reminder
0 2 * * * curl -s -X POST -H "Authorization: Bearer <token>" https://<你的網址>/api/jobs/daily-reminder > /dev/null 2>&1

# 每 5 分鐘 keep-warm 防 .NET app 被 swap 換出造成 cold start
*/5 * * * * curl -s -o /dev/null https://<你的網址>/api/discord/interactions -X POST -H "Content-Type: application/json" -d '{"type":1}'
```

`0 2 * * *` 是 UTC 02:00 = 台灣 10:00（VM 時區設 Asia/Taipei 也可寫 `0 10 * * *`）。

### 部署新版本

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

### DB 備份

VM 上設 cron 每天 `pg_dump` Neon DB dump 一份留 7 天，雙保險（Neon 免費版本身就有 7 天 PITR）：

```bash
0 3 * * * pg_dump "$NEON_CONNECTION_STRING" | gzip > /home/ubuntu/backups/goldfish-$(date +\%Y\%m\%d).sql.gz && find /home/ubuntu/backups -name "goldfish-*.sql.gz" -mtime +7 -delete
```

## 排錯

| 症狀 | 排查 |
|---|---|
| Discord 互動回 401 | 簽章驗證失敗，檢查 `Discord:PublicKey` 是否填對，也檢查 VM 時間是否偏移過大（簽章帶時間戳，±5 分鐘容忍） |
| Daily reminder 沒跑 | `journalctl -u goldfish --since today \| grep RunDailyReminder` 看有沒被觸發，沒的話檢查 cron log 與 `Jobs:AuthToken` 是否對 |
| 通知沒送到 | 查 `notification_logs` 表 `status='fail'` 的 row 看 `error_message` 欄位，通常是 Discord API 4xx 或 channel 被刪 |
| 帳號綁定後沒收到網頁連結 | 該 user 私人頻道是否成功建立，也查 `notification_logs` 看綁定通知有無 fail row |
| 第一次互動慢（~2 秒） | .NET app cold start 正常，keep-warm cron 5 分鐘 ping 一次可緩解 |
| Settings 頁面 401 跳回首頁 | cookie 過期（1 小時 sliding 關閉），回 Discord 重點按鈕拿新 token |

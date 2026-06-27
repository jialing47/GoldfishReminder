using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoldfishReminder.Api.Background;
using GoldfishReminder.Api.Security;
using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace GoldfishReminder.Api.Controllers;

[ApiController]
[Route("api/discord/interactions")]
public class DiscordInteractionsController : ControllerBase
{
    private const string LinkCustomId = "gr_link";
    private const string MarkPaidPrefix = "gr_mark_paid:";
    private const string BillAmountPrefix = "gr_bill_amount:";
    private const string BillAmountModalPrefix = "gr_bill_amount_modal:";
    private const string BalanceCommandName = "balance";
    private const string BalanceSelectCustomId = "gr_balance_select";
    private const string BalanceModalPrefix = "gr_balance_modal:";
    private const string BalanceModalInputId = "newBalance";

    // Discord interaction 共用的 JSON 設定 static readonly 讓內部 type metadata cache 跨請求重用 大幅減少反射成本
    private static readonly JsonSerializerOptions InteractionJsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CreditBillWorkflow creditBillWorkflow;
    private readonly IBackgroundTaskQueue taskQueue;
    private readonly IDiscordSignatureVerifier discordSignatureVerifier;
    private readonly IUserService userService;

    public DiscordInteractionsController(
        CreditBillWorkflow creditBillWorkflow,
        IBackgroundTaskQueue taskQueue,
        IDiscordSignatureVerifier discordSignatureVerifier,
        IUserService userService)
    {
        this.creditBillWorkflow = creditBillWorkflow;
        this.taskQueue = taskQueue;
        this.discordSignatureVerifier = discordSignatureVerifier;
        this.userService = userService;
    }

    // 接收 Discord interaction
    [HttpPost]
    public async Task<IActionResult> HandleAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();

        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        if (!discordSignatureVerifier.Verify(Request, rawBody))
        {
            return Unauthorized();
        }

        var request = JsonSerializer.Deserialize<DiscordInteractionRequest>(rawBody, InteractionJsonOptions);

        if (request == null)
        {
            return BadRequest(new { message = "Invalid payload." });
        }

        if (request.Type == 1)
        {
            return Ok(new { type = 1 });
        }

        if (request.Type == 3)
        {
            if (request.Data == null)
            {
                return BadRequest(new { message = "Interaction data is required." });
            }

            var customId = request.Data.CustomId;

            if (string.IsNullOrWhiteSpace(customId))
            {
                return BadRequest(new { message = "CustomId is required." });
            }

            if (string.Equals(customId, LinkCustomId, StringComparison.Ordinal))
            {
                var discordUserId = request.GetDiscordUserId();

                if (string.IsNullOrWhiteSpace(request.ApplicationId))
                {
                    return BadRequest(new { message = "ApplicationId is required." });
                }

                if (string.IsNullOrWhiteSpace(request.Token))
                {
                    return BadRequest(new { message = "Interaction token is required." });
                }

                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    return BadRequest(new { message = "Discord user id is required." });
                }

                var applicationId = request.ApplicationId;
                var interactionToken = request.Token;
                var discordDisplayName = request.GetDiscordDisplayName();

                taskQueue.Enqueue(async (serviceProvider, stoppingToken) =>
                {
                    var discordSettingsLinkService = serviceProvider.GetRequiredService<IDiscordSettingsLinkService>();

                    await discordSettingsLinkService.SendSettingsLinkAsync(
                        new DiscordSettingsLinkRequest
                        {
                            ApplicationId = applicationId,
                            InteractionToken = interactionToken,
                            DiscordUserId = discordUserId,
                            DiscordDisplayName = discordDisplayName
                        },
                        stoppingToken);
                });

                return Ok(new
                {
                    type = 5,
                    data = new { flags = 64 }
                });
            }

            if (customId.StartsWith(MarkPaidPrefix, StringComparison.Ordinal))
            {
                var billIdText = customId[MarkPaidPrefix.Length..];

                if (!Guid.TryParse(billIdText, out var billId))
                {
                    return BadRequest(new { message = "Invalid bill id." });
                }

                var discordUserId = request.GetDiscordUserId();
                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    return Ok(BuildEphemeralMessage("無法識別使用者"));
                }

                var user = await userService.GetByDiscordIdAsync(discordUserId, cancellationToken);
                if (user == null)
                {
                    return Ok(BuildEphemeralMessage("尚未綁定 請先使用 /gr_link"));
                }

                try
                {
                    await creditBillWorkflow.MarkBillPaidAsync(billId, user.Id, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    return Ok(BuildEphemeralMessage("此帳單不屬於你"));
                }

                return Ok(BuildEphemeralMessage("已標記為繳費完成"));
            }

            if (customId.StartsWith(BillAmountPrefix, StringComparison.Ordinal))
            {
                var billIdText = customId[BillAmountPrefix.Length..];

                if (!Guid.TryParse(billIdText, out var billId))
                {
                    return BadRequest(new { message = "Invalid bill id." });
                }

                return Ok(BuildBillAmountModalResponse(billId));
            }

            // /balance 下拉選單選定帳戶 直接回更新餘額 modal 此步零 DB modal 不能 defer
            if (string.Equals(customId, BalanceSelectCustomId, StringComparison.Ordinal))
            {
                var accountIdText = request.Data.GetFirstSelectedValue();

                if (!Guid.TryParse(accountIdText, out var accountId))
                {
                    return Ok(BuildEphemeralMessage("帳戶未選擇或格式錯誤"));
                }

                return Ok(BuildBalanceModalResponse(accountId));
            }
        }

        // type 2 slash command 目前只支援 /balance
        if (request.Type == 2)
        {
            if (request.Data == null || !string.Equals(request.Data.Name, BalanceCommandName, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Unsupported command." });
            }

            var discordUserId = request.GetDiscordUserId();
            if (string.IsNullOrWhiteSpace(discordUserId))
            {
                return BadRequest(new { message = "Discord user id is required." });
            }

            if (string.IsNullOrWhiteSpace(request.ApplicationId))
            {
                return BadRequest(new { message = "ApplicationId is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { message = "Interaction token is required." });
            }

            var applicationId = request.ApplicationId;
            var interactionToken = request.Token;

            // 查使用者與帳戶都要跨區打 DB 為避開首次回應 3 秒硬限制先 defer 再用背景佇列補上帳戶下拉選單
            taskQueue.Enqueue(async (serviceProvider, stoppingToken) =>
            {
                var scopedUserService = serviceProvider.GetRequiredService<IUserService>();
                var creditBillDataService = serviceProvider.GetRequiredService<ICreditBillDataService>();
                var discordApiClient = serviceProvider.GetRequiredService<IDiscordApiClient>();

                try
                {
                    var user = await scopedUserService.GetByDiscordIdAsync(discordUserId, stoppingToken);
                    if (user == null)
                    {
                        await discordApiClient.SendFollowupAsync(applicationId, interactionToken, "請先在綁定頻道點擊 gr_link 完成初始化", true, stoppingToken);
                        return;
                    }

                    var accounts = await creditBillDataService.GetUserAccountsAsync(user.Id, stoppingToken);
                    if (accounts.Count == 0)
                    {
                        await discordApiClient.SendFollowupAsync(applicationId, interactionToken, "尚無可更新餘額的帳戶", true, stoppingToken);
                        return;
                    }

                    var payload = BuildBalanceSelectPayload(accounts);
                    await discordApiClient.SendFollowupPayloadAsync(applicationId, interactionToken, payload, stoppingToken);
                }
                catch (Exception)
                {
                    // 查詢或送出失敗時回使用者通用錯誤句 避免卡在「思考中」 再 rethrow 讓 hosted service 記錄錯誤
                    await discordApiClient.SendFollowupAsync(applicationId, interactionToken, "查詢帳戶失敗 請稍後再試", true, stoppingToken);
                    throw;
                }
            });

            return Ok(new
            {
                type = 5,
                data = new { flags = 64 }
            });
        }

        if (request.Type == 5)
        {
            if (request.Data == null)
            {
                return BadRequest(new { message = "Interaction data is required." });
            }

            var customId = request.Data.CustomId;

            if (string.IsNullOrWhiteSpace(customId))
            {
                return BadRequest(new { message = "CustomId is required." });
            }

            if (customId.StartsWith(BillAmountModalPrefix, StringComparison.Ordinal))
            {
                var billIdText = customId[BillAmountModalPrefix.Length..];

                if (!Guid.TryParse(billIdText, out var billId))
                {
                    return BadRequest(new { message = "Invalid bill id." });
                }

                var billAmountText = request.Data.GetFirstValue("billAmount");

                if (!TryParseBillAmount(billAmountText, out var billAmount))
                {
                    return BadRequest(new { message = "Invalid bill amount." });
                }

                var discordUserId = request.GetDiscordUserId();
                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    return Ok(BuildEphemeralMessage("無法識別使用者"));
                }

                var user = await userService.GetByDiscordIdAsync(discordUserId, cancellationToken);
                if (user == null)
                {
                    return Ok(BuildEphemeralMessage("尚未綁定 請先使用 /gr_link"));
                }

                try
                {
                    var action = await creditBillWorkflow.ConfirmBillAmountAsync(billId, user.Id, billAmount, cancellationToken);
                    return Ok(BuildWorkflowResponse(action));
                }
                catch (UnauthorizedAccessException)
                {
                    return Ok(BuildEphemeralMessage("此帳單不屬於你"));
                }
            }

            if (customId.StartsWith(BalanceModalPrefix, StringComparison.Ordinal))
            {
                var accountIdText = customId[BalanceModalPrefix.Length..];

                if (!Guid.TryParse(accountIdText, out var accountId))
                {
                    return BadRequest(new { message = "Invalid account id." });
                }

                var newBalanceText = request.Data.GetFirstValue(BalanceModalInputId);

                if (!TryParseBillAmount(newBalanceText, out var newBalance))
                {
                    return Ok(BuildEphemeralMessage("餘額格式錯誤"));
                }

                // 驗證 Discord user 擁有此帳戶 防止偽造 interaction 改別人餘額
                var discordUserId = request.GetDiscordUserId();
                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    return BadRequest(new { message = "Discord user id is required." });
                }

                if (string.IsNullOrWhiteSpace(request.ApplicationId))
                {
                    return BadRequest(new { message = "ApplicationId is required." });
                }

                if (string.IsNullOrWhiteSpace(request.Token))
                {
                    return BadRequest(new { message = "Interaction token is required." });
                }

                var applicationId = request.ApplicationId;
                var interactionToken = request.Token;

                // 更新餘額與重跑提醒決策都要跨區打 DB 先 defer 再用背景佇列補回結果
                taskQueue.Enqueue(async (serviceProvider, stoppingToken) =>
                {
                    var scopedUserService = serviceProvider.GetRequiredService<IUserService>();
                    var bankAccountService = serviceProvider.GetRequiredService<IBankAccountService>();
                    var scopedWorkflow = serviceProvider.GetRequiredService<CreditBillWorkflow>();
                    var discordApiClient = serviceProvider.GetRequiredService<IDiscordApiClient>();

                    // 標記餘額是否已成功寫入 DB 一旦為 true 後續即使提醒重算失敗也不再回報「更新失敗」避免矛盾訊息
                    var balanceUpdated = false;

                    try
                    {
                        var user = await scopedUserService.GetByDiscordIdAsync(discordUserId, stoppingToken);
                        if (user == null)
                        {
                            await discordApiClient.SendFollowupAsync(applicationId, interactionToken, "請先完成初始化", true, stoppingToken);
                            return;
                        }

                        // 更新餘額 此步成功即代表餘額已寫入 DB
                        var updated = await bankAccountService.UpdateBalanceAsync(accountId, user.Id, newBalance, stoppingToken);
                        balanceUpdated = true;

                        // 餘額已更新 先回報成功 後續提醒重算即使失敗 餘額已更新仍是事實
                        await discordApiClient.SendFollowupAsync(applicationId, interactionToken, $"已更新 {updated.AccountName} 餘額為 {newBalance:N0}", true, stoppingToken);

                        // 重跑該帳戶底下所有帳單的提醒決策
                        await scopedWorkflow.ProcessAccountAsync(accountId, stoppingToken);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is KeyNotFoundException)
                    {
                        // 帳戶不屬於此使用者、或根本不存在（含 custom_id 被竄改成不存在的 id）屬預期內狀況 不視為系統錯誤 不 rethrow 只在尚未回報成功時回此訊息
                        if (!balanceUpdated)
                        {
                            await discordApiClient.SendFollowupAsync(applicationId, interactionToken, "找不到此帳戶或帳戶已停用", true, stoppingToken);
                        }
                    }
                    catch (Exception)
                    {
                        // 僅在餘額尚未更新前失敗才回報失敗 避免在已回報成功後又送矛盾訊息 再 rethrow 讓 hosted service 記錄錯誤
                        if (!balanceUpdated)
                        {
                            await discordApiClient.SendFollowupAsync(applicationId, interactionToken, "更新餘額失敗 請稍後再試", true, stoppingToken);
                        }
                        throw;
                    }
                });

                return Ok(new
                {
                    type = 5,
                    data = new { flags = 64 }
                });
            }
        }

        return BadRequest(new { message = "Unsupported interaction type." });
    }

    // 解析輸入金額 允許逗號與空白
    private static bool TryParseBillAmount(string? value, out int billAmount)
    {
        billAmount = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value
            .Replace(",", string.Empty)
            .Replace("\uFF0C", string.Empty)
            .Trim();

        if (!int.TryParse(normalized, out billAmount))
        {
            return false;
        }

        return billAmount >= 0;
    }

    // 建立 workflow 回覆 ActionType=None 為純成功訊息用 ephemeral 避免頻道噪音 其他類型含按鈕或警告維持永久訊息
    private static object BuildWorkflowResponse(WorkflowAction action)
    {
        var components = action.Components;
        if (components == null)
        {
            components = Array.Empty<object>();
        }

        if (action.ActionType == BillActionType.None)
        {
            return new
            {
                type = 4,
                data = new
                {
                    content = action.Message,
                    components = components,
                    flags = 64
                }
            };
        }

        return new
        {
            type = 4,
            data = new
            {
                content = action.Message,
                components = components
            }
        };
    }

    // 建立輸入帳單金額 modal 回應
    private static object BuildBillAmountModalResponse(Guid billId)
    {
        return new
        {
            type = 9,
            data = new
            {
                custom_id = $"gr_bill_amount_modal:{billId}",
                title = "輸入帳單金額",
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new
                            {
                                type = 4,
                                custom_id = "billAmount",
                                label = "帳單金額",
                                style = 1,
                                min_length = 1,
                                max_length = 10,
                                required = true,
                                placeholder = "請輸入本期帳單金額"
                            }
                        }
                    }
                }
            }
        };
    }

    // 建立更新餘額 modal 回應 此步零 DB 故為通用版 customId 帶 accountId 讓 submit 知道要更新哪個帳戶 帳戶名只在最後成功訊息出現
    private static object BuildBalanceModalResponse(Guid accountId)
    {
        return new
        {
            type = 9,
            data = new
            {
                custom_id = $"{BalanceModalPrefix}{accountId}",
                title = "更新帳戶餘額",
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new
                            {
                                type = 4,
                                custom_id = BalanceModalInputId,
                                label = "新餘額",
                                style = 1,
                                min_length = 1,
                                max_length = 10,
                                required = true,
                                placeholder = "請輸入新餘額"
                            }
                        }
                    }
                }
            }
        };
    }

    // 建立 /balance 帳戶下拉選單 followup payload 帶 String Select Menu 選項最多 25 ephemeral 由 flags=64 控制
    private static object BuildBalanceSelectPayload(IReadOnlyList<UserAccountOption> accounts)
    {
        var options = new List<object>();

        foreach (var account in accounts.Take(25))
        {
            var label = $"{account.BankName} {account.AccountName} ({account.Balance:N0})";

            // Discord select option label 上限 100 字
            if (label.Length > 100)
            {
                label = label[..100];
            }

            options.Add(new
            {
                label = label,
                value = account.Id.ToString()
            });
        }

        var selectMenu = new
        {
            type = 3,
            custom_id = BalanceSelectCustomId,
            placeholder = "選擇要更新餘額的帳戶",
            options = options
        };

        var actionRow = new
        {
            type = 1,
            components = new object[] { selectMenu }
        };

        return new
        {
            content = "請選擇要更新餘額的帳戶",
            components = new object[] { actionRow },
            flags = 64
        };
    }

    // 建立 ephemeral 純文字回應 僅使用者看得到
    private static object BuildEphemeralMessage(string content)
    {
        return new
        {
            type = 4,
            data = new
            {
                content = content,
                flags = 64
            }
        };
    }

}

// Discord interaction request
public class DiscordInteractionRequest
{
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("member")]
    public DiscordInteractionMember? Member { get; set; }

    [JsonPropertyName("user")]
    public DiscordInteractionUser? User { get; set; }

    [JsonPropertyName("data")]
    public DiscordInteractionData? Data { get; set; }

    public string? GetDiscordUserId()
    {
        if (Member?.User != null && !string.IsNullOrWhiteSpace(Member.User.Id))
        {
            return Member.User.Id;
        }

        if (User != null && !string.IsNullOrWhiteSpace(User.Id))
        {
            return User.Id;
        }

        return null;
    }

    public string? GetDiscordDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(Member?.Nick))
        {
            return Member.Nick;
        }

        if (Member?.User != null)
        {
            if (!string.IsNullOrWhiteSpace(Member.User.GlobalName))
            {
                return Member.User.GlobalName;
            }

            if (!string.IsNullOrWhiteSpace(Member.User.Username))
            {
                return Member.User.Username;
            }
        }

        if (User != null)
        {
            if (!string.IsNullOrWhiteSpace(User.GlobalName))
            {
                return User.GlobalName;
            }

            if (!string.IsNullOrWhiteSpace(User.Username))
            {
                return User.Username;
            }
        }

        return null;
    }
}

// Discord interaction member
public class DiscordInteractionMember
{
    [JsonPropertyName("nick")]
    public string? Nick { get; set; }

    [JsonPropertyName("user")]
    public DiscordInteractionUser? User { get; set; }
}

// Discord interaction user
public class DiscordInteractionUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("global_name")]
    public string? GlobalName { get; set; }
}

// Discord interaction data
public class DiscordInteractionData
{
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("components")]
    public List<DiscordInteractionComponent>? Components { get; set; }

    [JsonPropertyName("values")]
    public List<string>? Values { get; set; }

    // 取出下拉選單第一個選擇值 type 3 message component 的選擇結果放在 data.values
    public string? GetFirstSelectedValue()
    {
        if (Values == null || Values.Count == 0)
        {
            return null;
        }

        return Values[0];
    }

    public string? GetFirstValue(string customId)
    {
        if (Components == null)
        {
            return null;
        }

        foreach (var row in Components)
        {
            if (row.Components == null)
            {
                continue;
            }

            foreach (var component in row.Components)
            {
                if (component.CustomId == customId)
                {
                    return component.Value;
                }
            }
        }

        return null;
    }
}

// Discord interaction component
public class DiscordInteractionComponent
{
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("components")]
    public List<DiscordInteractionComponent>? Components { get; set; }
}

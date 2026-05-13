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
    private const string BalanceAccountOptionName = "account";
    private const string BalanceModalPrefix = "gr_balance_modal:";
    private const string BalanceModalInputId = "newBalance";

    private readonly CreditBillWorkflow creditBillWorkflow;
    private readonly IBackgroundTaskQueue taskQueue;
    private readonly IDiscordSignatureVerifier discordSignatureVerifier;
    private readonly IUserService userService;
    private readonly IBankAccountService bankAccountService;
    private readonly ICreditBillDataService creditBillDataService;

    public DiscordInteractionsController(
        CreditBillWorkflow creditBillWorkflow,
        IBackgroundTaskQueue taskQueue,
        IDiscordSignatureVerifier discordSignatureVerifier,
        IUserService userService,
        IBankAccountService bankAccountService,
        ICreditBillDataService creditBillDataService)
    {
        this.creditBillWorkflow = creditBillWorkflow;
        this.taskQueue = taskQueue;
        this.discordSignatureVerifier = discordSignatureVerifier;
        this.userService = userService;
        this.bankAccountService = bankAccountService;
        this.creditBillDataService = creditBillDataService;
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

        var request = JsonSerializer.Deserialize<DiscordInteractionRequest>(
            rawBody,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

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

            var user = await userService.GetByDiscordIdAsync(discordUserId, cancellationToken);
            if (user == null)
            {
                return Ok(BuildEphemeralMessage("請先在綁定頻道點擊 gr_link 完成初始化"));
            }

            var accountIdText = request.Data.GetOptionValue(BalanceAccountOptionName);
            if (!Guid.TryParse(accountIdText, out var accountId))
            {
                return Ok(BuildEphemeralMessage("帳戶未選擇或格式錯誤"));
            }

            var accounts = await creditBillDataService.GetUserAccountsAsync(user.Id, cancellationToken);
            var selected = accounts.FirstOrDefault(x => x.Id == accountId);
            if (selected == null)
            {
                return Ok(BuildEphemeralMessage("找不到此帳戶或帳戶已停用"));
            }

            return Ok(BuildBalanceModalResponse(selected));
        }

        // type 4 autocomplete 動態回覆帳戶選項
        if (request.Type == 4)
        {
            if (request.Data == null || !string.Equals(request.Data.Name, BalanceCommandName, StringComparison.Ordinal))
            {
                return Ok(BuildAutocompleteResponse(Array.Empty<UserAccountOption>()));
            }

            var discordUserId = request.GetDiscordUserId();
            if (string.IsNullOrWhiteSpace(discordUserId))
            {
                return Ok(BuildAutocompleteResponse(Array.Empty<UserAccountOption>()));
            }

            var user = await userService.GetByDiscordIdAsync(discordUserId, cancellationToken);
            if (user == null)
            {
                return Ok(BuildAutocompleteResponse(Array.Empty<UserAccountOption>()));
            }

            var accounts = await creditBillDataService.GetUserAccountsAsync(user.Id, cancellationToken);
            var focusedValue = request.Data.GetOptionValue(BalanceAccountOptionName, focusedOnly: true);
            if (focusedValue == null)
            {
                focusedValue = string.Empty;
            }
            var filtered = FilterAccounts(accounts, focusedValue);

            return Ok(BuildAutocompleteResponse(filtered));
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

                var user = await userService.GetByDiscordIdAsync(discordUserId, cancellationToken);
                if (user == null)
                {
                    return Ok(BuildEphemeralMessage("請先完成初始化"));
                }

                try
                {
                    // 更新餘額後重跑該帳戶底下所有帳單的提醒決策
                    var updated = await bankAccountService.UpdateBalanceAsync(accountId, user.Id, newBalance, cancellationToken);
                    await creditBillWorkflow.ProcessAccountAsync(accountId, cancellationToken);

                    return Ok(BuildEphemeralMessage($"已更新 {updated.AccountName} 餘額為 {newBalance:N0}"));
                }
                catch (UnauthorizedAccessException)
                {
                    return Ok(BuildEphemeralMessage("找不到此帳戶或帳戶已停用"));
                }
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

    // 建立更新餘額 modal 回應 customId 帶 accountId 讓後續 submit 知道要更新哪個帳戶
    private static object BuildBalanceModalResponse(UserAccountOption account)
    {
        var placeholder = $"目前餘額 {account.Balance:N0}";

        return new
        {
            type = 9,
            data = new
            {
                custom_id = $"{BalanceModalPrefix}{account.Id}",
                title = $"更新 {account.AccountName} 餘額",
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
                                placeholder = placeholder
                            }
                        }
                    }
                }
            }
        };
    }

    // 建立 autocomplete 回應 choices 最多 25
    private static object BuildAutocompleteResponse(IReadOnlyList<UserAccountOption> accounts)
    {
        var choices = new List<object>(accounts.Count);

        foreach (var account in accounts)
        {
            var name = $"{account.BankName} {account.AccountName} ({account.Balance:N0})";

            // Discord autocomplete choice name 上限 100 字
            if (name.Length > 100)
            {
                name = name[..100];
            }

            choices.Add(new
            {
                name = name,
                value = account.Id.ToString()
            });
        }

        return new
        {
            type = 8,
            data = new
            {
                choices = choices
            }
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

    // 以 user focused 輸入過濾帳戶選項 大小寫不敏感 最多 25 筆
    private static IReadOnlyList<UserAccountOption> FilterAccounts(IReadOnlyList<UserAccountOption> accounts, string focused)
    {
        IEnumerable<UserAccountOption> filtered = accounts;

        if (!string.IsNullOrWhiteSpace(focused))
        {
            var keyword = focused.Trim();
            filtered = accounts.Where(x =>
                x.AccountName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || x.BankCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || x.BankName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.Take(25).ToList();
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

    [JsonPropertyName("options")]
    public List<DiscordCommandOption>? Options { get; set; }

    [JsonPropertyName("components")]
    public List<DiscordInteractionComponent>? Components { get; set; }

    // 取出指定 option 的值 若 focused=true 只取 focused
    public string? GetOptionValue(string optionName, bool focusedOnly = false)
    {
        if (Options == null)
        {
            return null;
        }

        foreach (var option in Options)
        {
            if (option.Name != optionName)
            {
                continue;
            }

            if (focusedOnly && !option.Focused)
            {
                return null;
            }

            return option.Value;
        }

        return null;
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

// slash command option
public class DiscordCommandOption
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("focused")]
    public bool Focused { get; set; }
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

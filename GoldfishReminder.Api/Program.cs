using GoldfishReminder.Api.Background;
using GoldfishReminder.Api.Security;
using GoldfishReminder.Api.Jobs;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Application.Workflows;
using GoldfishReminder.Infrastructure.Configuration;
using GoldfishReminder.Infrastructure.Persistence;
using GoldfishReminder.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

// MVC / Razor / Swagger
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// PostgreSQL DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Options 綁定
builder.Services.Configure<WebOptions>(builder.Configuration.GetSection("Web"));
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection("Jobs"));

// 外部 API client
builder.Services.AddHttpClient<IDiscordApiClient, DiscordApiClient>();

// 基礎 provider / verifier
builder.Services.AddSingleton<IWebUrlProvider, WebUrlProvider>();
builder.Services.AddSingleton<IDiscordSettingsProvider, DiscordSettingsProvider>();
builder.Services.AddSingleton<IDiscordSignatureVerifier, DiscordSignatureVerifier>();

// 資料服務
builder.Services.AddScoped<ICreditBillService, CreditBillService>();
builder.Services.AddScoped<ICreditSettingService, CreditSettingService>();
builder.Services.AddScoped<IBankAccountService, BankAccountService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IWebLinkTokenService, WebLinkTokenService>();

// 查詢 / 通知 / 頁面服務
builder.Services.AddScoped<ISettingsPageService, SettingsPageService>();
builder.Services.AddScoped<ICreditBillDataService, CreditBillDataService>();
builder.Services.AddScoped<INotificationSender, NotificationSender>();
builder.Services.AddScoped<INotificationLogService, NotificationLogService>();

// Discord 流程
builder.Services.AddScoped<IDiscordOnboardingService, DiscordOnboardingService>();
builder.Services.AddScoped<IDiscordSettingsLinkService, DiscordSettingsLinkService>();

// 核心 workflow / job
builder.Services.AddScoped<NotificationWorkflow>();
builder.Services.AddScoped<CreditBillWorkflow>();
builder.Services.AddScoped<DailyReminderJob>();

// 背景工作
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<TaskQueueHostedService>();

var app = builder.Build();

// production 用自訂 Error 頁 dev 用內建詳細頁
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
}
// 非 2xx 狀態碼 例如 404 405 重新導到 /Error/{statusCode}
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseHttpsRedirection();

// 啟用 wwwroot 靜態檔 讓 /logo.png /favicon.png 等可被存取
app.UseStaticFiles();

// 路由
app.MapControllers();
app.MapRazorPages();

app.Run();

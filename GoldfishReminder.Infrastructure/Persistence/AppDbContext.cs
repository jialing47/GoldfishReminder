using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using GoldfishReminder.Domain.Entities;
namespace GoldfishReminder.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<CreditSetting> CreditSettings => Set<CreditSetting>();
    public DbSet<CreditBill> CreditBills => Set<CreditBill>();
    public DbSet<WebLinkToken> WebLinkTokens => Set<WebLinkToken>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUsers(modelBuilder);
        ConfigureBanks(modelBuilder);
        ConfigureBankAccounts(modelBuilder);
        ConfigureCreditSettings(modelBuilder);
        ConfigureCreditBills(modelBuilder);
        ConfigureWebLinkTokens(modelBuilder);
        ConfigureNotificationLogs(modelBuilder);
    }

    //users mapping
    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name").IsRequired();
            entity.Property(x => x.DiscordUserId).HasColumnName("discord_user_id");
            entity.Property(x => x.DiscordPrivateChannelId).HasColumnName("discord_private_channel_id");

            // 若你想在 EF model 也描述 unique + filter（可選）
            entity.HasIndex(x => x.DiscordUserId)
                .IsUnique()
                .HasDatabaseName("ux_users_discord_user_id")
                .HasFilter("\"discord_user_id\" IS NOT NULL");

            entity.HasIndex(x => x.DiscordPrivateChannelId)
                .IsUnique()
                .HasDatabaseName("ux_users_discord_private_channel_id")
                .HasFilter("\"discord_private_channel_id\" IS NOT NULL");
        });
    }

    //banks mapping
    private static void ConfigureBanks(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bank>(entity =>
        {
            entity.ToTable("banks");

            entity.HasKey(x => x.BankCode).HasName("banks_pkey");

            entity.Property(x => x.BankCode)
                .HasColumnName("bank_code")
                .HasMaxLength(10);

            entity.Property(x => x.BankName)
                .HasColumnName("bank_name")
                .IsRequired();

            entity.HasIndex(x => x.BankName)
                .IsUnique()
                .HasDatabaseName("ux_banks_bank_name");
        });
    }

    //bank_accounts mapping
    private static void ConfigureBankAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.ToTable("bank_accounts");

            entity.HasKey(x => x.Id)
                .HasName("bank_accounts_pkey");

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(x => x.BankCode)
                .HasColumnName("bank_code")
                .HasMaxLength(10)
                .IsRequired();

            entity.Property(x => x.AccountName)
                .HasColumnName("account_name")
                .IsRequired();

            entity.Property(x => x.AccountType)
                .HasColumnName("account_type")
                .IsRequired();

            entity.Property(x => x.Enabled)
                .HasColumnName("enabled")
                .IsRequired();

            entity.Property(x => x.Balance)
                .HasColumnName("balance")
                .HasColumnType("integer")
                .IsRequired();

            entity.Property(x => x.BalanceUpdatedAt)
                .HasColumnName("balance_updated_at");

            entity.HasIndex(x => x.UserId)
                .HasDatabaseName("ix_bank_accounts_user_id");

            entity.HasIndex(x => x.BankCode)
                .HasDatabaseName("ix_bank_accounts_bank_code");

            entity.HasIndex(x => new { x.UserId, x.BankCode })
                .HasDatabaseName("ix_bank_accounts_user_id_bank_code");
        });
    }

    //credit_settings mapping
    private static void ConfigureCreditSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreditSetting>(entity =>
        {
            entity.ToTable("credit_settings");

            entity.HasKey(x => x.Id)
                .HasName("credit_settings_pkey");

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(x => x.BankCode)
                .HasColumnName("bank_code")
                .HasMaxLength(10)
                .IsRequired();

            entity.Property(x => x.StatementDay)
                .HasColumnName("statement_day")
                .IsRequired();

            entity.Property(x => x.PaymentDueDay)
                .HasColumnName("payment_due_day")
                .IsRequired();

            entity.Property(x => x.PaymentBankAccountId)
                .HasColumnName("payment_bank_account_id");

            entity.Property(x => x.Enabled).HasColumnName("enabled");

            entity.HasOne(x => x.PaymentBankAccount)
                .WithMany()
                .HasForeignKey(x => x.PaymentBankAccountId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_credit_settings_payment_bank_accounts");

            entity.HasIndex(x => x.PaymentBankAccountId)
                .HasDatabaseName("ix_credit_settings_payment_bank_account_id");

            entity.HasIndex(x => x.BankCode)
                .HasDatabaseName("ix_credit_settings_bank_code");

            entity.HasIndex(x => new { x.UserId, x.BankCode })
                .IsUnique()
                .HasDatabaseName("ux_credit_settings_user_id_bank_code");
        });
    }

    //credit_bills mapping
    private static void ConfigureCreditBills(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreditBill>(entity =>
        {
            entity.ToTable("credit_bills");

            entity.HasKey(x => x.Id)
                .HasName("credit_bills_pkey");

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(x => x.BankCode)
                .HasColumnName("bank_code")
                .HasMaxLength(10)
                .IsRequired();

            entity.Property(x => x.BillYear)
                .HasColumnName("bill_year")
                .IsRequired();

            entity.Property(x => x.BillMonth)
                .HasColumnName("bill_month")
                .IsRequired();

            entity.Property(x => x.StatementDay)
                .HasColumnName("statement_day")
                .IsRequired();

            entity.Property(x => x.PaymentDueDay)
                .HasColumnName("payment_due_day")
                .IsRequired();

            entity.Property(x => x.BillAmount)
                .HasColumnName("bill_amount");

            entity.Property(x => x.AmountConfirmed)
                .HasColumnName("amount_confirmed")
                .IsRequired();

            entity.Property(x => x.Paid)
                .HasColumnName("paid")
                .IsRequired();

            entity.HasIndex(x => x.StatementDay)
                .HasDatabaseName("ix_credit_bills_statement_day");

            entity.HasIndex(x => x.PaymentDueDay)
                .HasDatabaseName("ix_credit_bills_payment_due_day");

            entity.HasIndex(x => new { x.UserId, x.BankCode })
                .HasDatabaseName("ix_credit_bills_user_id_bank_code");

            entity.HasIndex(x => new { x.UserId, x.BankCode, x.BillYear, x.BillMonth })
                .IsUnique()
                .HasDatabaseName("ux_credit_bills_user_id_bank_code_bill_year_bill_month");
        });
    }

    private static void ConfigureWebLinkTokens(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WebLinkToken>();

        entity.ToTable("web_link_tokens");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.UserId).HasColumnName("user_id");
        entity.Property(x => x.TokenHash).HasColumnName("token_hash");
        entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        entity.Property(x => x.UsedAt).HasColumnName("used_at");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");

        // ConsumeAsync 以 TokenHash 查 token 必須有 index 否則 row 多時會 seq scan
        entity.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_web_link_tokens_token_hash");

        // 每個 user 同時只能有一個未使用的 token DB 層兜底 C# rotation 邏輯
        entity.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("ux_web_link_tokens_one_active_per_user")
            .HasFilter("\"used_at\" IS NULL");
    }

    //notification_logs mapping
    private static void ConfigureNotificationLogs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.ToTable("notification_logs");

            entity.HasKey(x => x.Id)
                .HasName("notification_logs_pkey");

            entity.Property(x => x.Id)
                .HasColumnName("id");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(x => x.NotificationType)
                .HasColumnName("notification_type")
                .IsRequired();

            entity.Property(x => x.TargetId)
                .HasColumnName("target_id");

            entity.Property(x => x.MessageContent)
                .HasColumnName("message_content")
                .IsRequired();

            entity.Property(x => x.Status)
                .HasColumnName("status")
                .IsRequired();

            entity.Property(x => x.ErrorMessage)
                .HasColumnName("error_message");

            entity.Property(x => x.SentAt)
                .HasColumnName("sent_at")
                .HasColumnType("timestamp with time zone");


            entity.HasIndex(x => x.UserId)
                .HasDatabaseName("ix_notification_logs_user_id");

            entity.HasIndex(x => x.NotificationType)
                .HasDatabaseName("ix_notification_logs_notification_type");

            entity.HasIndex(x => x.SentAt)
                .HasDatabaseName("ix_notification_logs_sent_at");

            entity.HasIndex(x => new { x.UserId, x.SentAt })
                .HasDatabaseName("ix_notification_logs_user_id_sent_at");
        });
    }
}

namespace GoldfishReminder.Infrastructure.Configuration;

// 排程 job endpoint 的 shared secret 配置
public class JobsOptions
{
    public string AuthToken { get; set; } = string.Empty;
}

public enum EnterpriseEnvironment { Dev, Staging, Prod }

public static class EnterpriseEnvironmentHelpers
{
    public const string MsBuildOrEnvName = "ASPIRE_TARGET_ENV";

    public static bool TryParse(string? raw, out EnterpriseEnvironment env)
    {
        env = EnterpriseEnvironment.Dev;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "dev":      env = EnterpriseEnvironment.Dev;     return true;
            case "staging":  env = EnterpriseEnvironment.Staging; return true;
            case "prod":
            case "production":
                           env = EnterpriseEnvironment.Prod;    return true;
            default:         return false;
        }
    }
}
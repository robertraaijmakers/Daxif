namespace XrmPackager.Core.Authentication;

/// <summary>
/// Helper class to load authentication configuration from environment variables.
/// </summary>
public static class AuthenticationConfigLoader
{
    /// <summary>
    /// Loads authentication configuration from environment variables.
    /// Supported environment variables:
    /// - DATAVERSE_URL (required)
    /// - DATAVERSE_AUTH_TYPE (OAuth, ClientSecret, Certificate, ConnectionString)
    /// - DATAVERSE_USERNAME (OAuth)
    /// - DATAVERSE_PASSWORD (OAuth)
    /// - DATAVERSE_APP_ID / DATAVERSE_CLIENT_ID (OAuth, ClientSecret, Certificate)
    /// - DATAVERSE_CLIENT_SECRET (ClientSecret)
    /// - DATAVERSE_THUMBPRINT (Certificate)
    /// - DATAVERSE_REDIRECT_URI (OAuth, default: http://localhost)
    /// - DATAVERSE_LOGIN_PROMPT (OAuth, optional: Auto, Never, Always)
    /// - DATAVERSE_LOGIN_HINT (OAuth, optional: preferred account/email for interactive login)
    /// - DATAVERSE_CONNECTION_STRING (ConnectionString)
    /// - DATAVERSE_TOKEN_CACHE (OAuth)
    /// - DATAVERSE_TIMEOUT_MINUTES (default: 2)
    /// </summary>
    /// <returns>Configured DataverseAuthConfig</returns>
    /// <exception cref="InvalidOperationException">If required environment variables are missing</exception>
    public static DataverseAuthConfig LoadFromEnvironment()
    {
        var url = GetRequiredEnvVar("DATAVERSE_URL");
        var authTypeStr = GetRequiredEnvVar("DATAVERSE_AUTH_TYPE");

        if (!Enum.TryParse<AuthenticationMethod>(authTypeStr, ignoreCase: true, out var authType))
        {
            throw new InvalidOperationException(
                $"Invalid DATAVERSE_AUTH_TYPE '{authTypeStr}'. Must be OAuth, ClientSecret, Certificate, or ConnectionString"
            );
        }

        var config = new DataverseAuthConfig
        {
            OrganizationUrl = new Uri(url),
            AuthMethod = authType,
        };

        // Load common optional settings
        config.Username = GetOptionalEnvVar("DATAVERSE_USERNAME");
        config.Password = GetOptionalEnvVar("DATAVERSE_PASSWORD");
        config.AppId =
            GetOptionalEnvVar("DATAVERSE_APP_ID") ?? GetOptionalEnvVar("DATAVERSE_CLIENT_ID");
        config.ClientSecret = GetOptionalEnvVar("DATAVERSE_CLIENT_SECRET");
        config.CertificateThumbprint = GetOptionalEnvVar("DATAVERSE_THUMBPRINT");
        config.RedirectUri = GetOptionalEnvVar("DATAVERSE_REDIRECT_URI") ?? "http://localhost";
        config.LoginPrompt = GetOptionalEnvVar("DATAVERSE_LOGIN_PROMPT");
        config.LoginHint = GetOptionalEnvVar("DATAVERSE_LOGIN_HINT");
        config.TokenCachePath = GetOptionalEnvVar("DATAVERSE_TOKEN_CACHE");
        config.ConnectionString = GetOptionalEnvVar("DATAVERSE_CONNECTION_STRING");
        config.RequireNewInstance =
            GetOptionalEnvVar("DATAVERSE_NEW_INSTANCE")?.ToLower() == "true";

        // Load timeout if specified
        if (int.TryParse(GetOptionalEnvVar("DATAVERSE_TIMEOUT_MINUTES"), out var timeoutMinutes))
        {
            config.ConnectionTimeout = TimeSpan.FromMinutes(timeoutMinutes);
        }

        return config;
    }

    /// <summary>
    /// Loads authentication configuration from environment variables, with a fallback to manual configuration.
    /// </summary>
    public static DataverseAuthConfig LoadWithFallback(
        Action<DataverseAuthConfig>? configureManually = null
    )
    {
        try
        {
            return LoadFromEnvironment();
        }
        catch (InvalidOperationException) when (configureManually != null)
        {
            var config = new DataverseAuthConfig();
            configureManually(config);
            return config;
        }
    }

    private static string GetRequiredEnvVar(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not set. Run 'xrmpackager help' for usage information."
            );
        }
        return value;
    }

    private static string? GetOptionalEnvVar(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}

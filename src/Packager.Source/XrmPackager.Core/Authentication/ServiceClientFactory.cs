namespace XrmPackager.Core.Authentication;

using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

/// <summary>
/// Factory for creating authenticated ServiceClient instances for Dataverse.
/// </summary>
public class ServiceClientFactory
{
    private readonly DataverseAuthConfig _config;
    private readonly ILogger _logger;

    public ServiceClientFactory(DataverseAuthConfig config, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? new ConsoleLogger(config.LogLevel);
    }

    /// <summary>
    /// Creates and returns an authenticated ServiceClient.
    /// </summary>
    /// <returns>An authenticated ServiceClient ready for operations</returns>
    /// <exception cref="InvalidOperationException">If authentication fails</exception>
    public ServiceClient CreateServiceClient()
    {
        _logger.Info("Connecting to Dataverse...");
        _logger.Info($"Connection target: {_config.OrganizationUrl}");
        _logger.Info($"Authentication method: {_config.AuthMethod}");
        _logger.Info($"Connection timeout: {_config.ConnectionTimeout.TotalSeconds:F0}s");
        _logger.Info($"Credential profile: {DescribeCredentialProfile()}");
        _logger.Verbose($"Redirect URI: {_config.RedirectUri}");
        _logger.Verbose(
            $"Token cache configured: {!string.IsNullOrWhiteSpace(_config.TokenCachePath)}"
        );

        ServiceClient.MaxConnectionTimeout = _config.ConnectionTimeout;
        _logger.Info("Step 1/4: Building connection string...");

        var connectionString = BuildConnectionString();
        _logger.Info("Step 2/4: Connection string built successfully.");
        _logger.Verbose($"Connection string shape: {DescribeConnectionString(connectionString)}");
        _logger.Info("Step 3/4: Creating ServiceClient instance...");

        var stopwatch = Stopwatch.StartNew();
        ServiceClient? client;
        try
        {
            client = new ServiceClient(connectionString);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(
                $"ServiceClient constructor threw after {stopwatch.ElapsedMilliseconds} ms: {ex.GetType().Name}: {ex.Message}"
            );

            if (ex.InnerException is not null)
            {
                _logger.Error(
                    $"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
                );
            }

            throw;
        }

        stopwatch.Stop();
        _logger.Info($"Step 4/4: ServiceClient created in {stopwatch.ElapsedMilliseconds} ms.");

        if (!client.IsReady)
        {
            var lastError = string.IsNullOrWhiteSpace(client.LastError)
                ? "<empty>"
                : client.LastError;
            var errorMessage = $"Failed to connect to Dataverse: {lastError}";
            _logger.Error(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        _logger.Info("✓ Connected successfully!");
        _logger.Verbose($"Organization: {client.ConnectedOrgFriendlyName}");
        _logger.Verbose($"Organization ID: {client.ConnectedOrgId}");
        _logger.Verbose($"Version: {client.ConnectedOrgVersion}");

        TryPersistLoginHint(client);

        return client;
    }

    private void TryPersistLoginHint(ServiceClient client)
    {
        if (_config.AuthMethod != AuthenticationMethod.OAuth)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_config.LoginHint))
        {
            LoginHintCache.Set(_config.OrganizationUrl, _config.LoginHint);
            return;
        }

        var discoveredLoginHint = ResolveAuthenticatedLoginHint(client);
        if (string.IsNullOrWhiteSpace(discoveredLoginHint))
        {
            return;
        }

        LoginHintCache.Set(_config.OrganizationUrl, discoveredLoginHint);
        _logger.Verbose(
            $"Cached OAuth login hint for {_config.OrganizationUrl.Host}: {discoveredLoginHint}"
        );
    }

    private static string? ResolveAuthenticatedLoginHint(ServiceClient client)
    {
        try
        {
            var whoAmIResponse = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
            var user = client.Retrieve(
                "systemuser",
                whoAmIResponse.UserId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(
                    "internalemailaddress",
                    "domainname",
                    "windowsliveid"
                )
            );

            var internalEmail = user.GetAttributeValue<string>("internalemailaddress");
            if (!string.IsNullOrWhiteSpace(internalEmail))
            {
                return internalEmail;
            }

            var domainName = user.GetAttributeValue<string>("domainname");
            if (!string.IsNullOrWhiteSpace(domainName) && domainName.Contains('@'))
            {
                return domainName;
            }

            var windowsLiveId = user.GetAttributeValue<string>("windowsliveid");
            if (!string.IsNullOrWhiteSpace(windowsLiveId) && windowsLiveId.Contains('@'))
            {
                return windowsLiveId;
            }
        }
        catch
        {
            // Continue with token-based fallback.
        }

        return TryResolveLoginHintFromAccessToken(client);
    }

    private static string? TryResolveLoginHintFromAccessToken(ServiceClient client)
    {
        try
        {
            var tokenProperty = client.GetType().GetProperty("CurrentAccessToken");
            var token = tokenProperty?.GetValue(client) as string;
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');

            while (payload.Length % 4 != 0)
            {
                payload += "=";
            }

            var payloadBytes = Convert.FromBase64String(payload);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            using var document = JsonDocument.Parse(payloadJson);

            return GetTokenClaim(document, "preferred_username")
                ?? GetTokenClaim(document, "upn")
                ?? GetTokenClaim(document, "email")
                ?? GetTokenClaim(document, "unique_name");
        }
        catch
        {
            return null;
        }
    }

    private static string? GetTokenClaim(JsonDocument document, string claimName)
    {
        if (!document.RootElement.TryGetProperty(claimName, out var claimValue))
        {
            return null;
        }

        if (claimValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = claimValue.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string DescribeCredentialProfile()
    {
        return _config.AuthMethod switch
        {
            AuthenticationMethod.OAuth =>
                $"OAuth (username: {!string.IsNullOrWhiteSpace(_config.Username)}, password: {!string.IsNullOrWhiteSpace(_config.Password)}, appId: {!string.IsNullOrWhiteSpace(_config.AppId)})",
            AuthenticationMethod.ClientSecret =>
                $"ClientSecret (clientId: {!string.IsNullOrWhiteSpace(_config.AppId)}, clientSecret: {!string.IsNullOrWhiteSpace(_config.ClientSecret)})",
            AuthenticationMethod.Certificate =>
                $"Certificate (clientId: {!string.IsNullOrWhiteSpace(_config.AppId)}, thumbprint: {!string.IsNullOrWhiteSpace(_config.CertificateThumbprint)})",
            AuthenticationMethod.ConnectionString =>
                $"ConnectionString (provided: {!string.IsNullOrWhiteSpace(_config.ConnectionString)})",
            _ => "Unknown",
        };
    }

    private static string DescribeConnectionString(string connectionString)
    {
        var keys = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0].Trim())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key)
            .ToArray();

        return keys.Length == 0
            ? "<empty>"
            : string.Join(", ", keys.Select(k => IsSensitiveKey(k) ? $"{k}=***" : k));
    }

    private static bool IsSensitiveKey(string key)
    {
        return key.Equals("Password", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ClientSecret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Password", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the connection string based on authentication method and configuration.
    /// </summary>
    private string BuildConnectionString()
    {
        var url = _config.OrganizationUrl.ToString();

        return _config.AuthMethod switch
        {
            AuthenticationMethod.OAuth => BuildOAuthConnectionString(url),
            AuthenticationMethod.ClientSecret => BuildClientSecretConnectionString(url),
            AuthenticationMethod.Certificate => BuildCertificateConnectionString(url),
            AuthenticationMethod.ConnectionString => _config.ConnectionString
                ?? throw new InvalidOperationException("Connection string not provided"),
            _ => throw new InvalidOperationException(
                $"Unsupported authentication method: {_config.AuthMethod}"
            ),
        };
    }

    /// <summary>
    /// Builds OAuth connection string.
    /// </summary>
    private string BuildOAuthConnectionString(string url)
    {
        if (string.IsNullOrEmpty(_config.AppId))
        {
            throw new InvalidOperationException("AppId is required for OAuth authentication");
        }

        var hasUsername = !string.IsNullOrWhiteSpace(_config.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(_config.Password);
        var loginHint = !string.IsNullOrWhiteSpace(_config.LoginHint)
            ? _config.LoginHint
            : _config.Username;

        if (!hasUsername && hasPassword)
        {
            _logger.Warning(
                "OAuth password is configured without username. Ignoring password and using interactive OAuth."
            );
            hasPassword = false;
        }

        if (hasUsername && !hasPassword)
        {
            _logger.Info(
                "OAuth username is configured without password. Using it as a login hint for interactive sign-in."
            );
        }

        if (!string.IsNullOrWhiteSpace(_config.LoginHint) && hasUsername)
        {
            _logger.Verbose(
                "OAuth login hint is set. It will be preferred over username for account selection."
            );
        }

        var loginPrompt = ResolveLoginPrompt(hasUsername && hasPassword);

        var parts = new List<string>
        {
            $"AuthType=OAuth",
            $"Url={url}",
            $"AppId={_config.AppId}",
            $"RedirectUri={_config.RedirectUri}",
            $"LoginPrompt={loginPrompt}",
        };

        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            parts.Add($"Username={loginHint}");
        }

        if (hasPassword)
        {
            parts.Add($"Password={_config.Password}");
        }

        if (!string.IsNullOrEmpty(_config.TokenCachePath))
        {
            parts.Add($"TokenCacheStorePath={_config.TokenCachePath}");
        }
        else
        {
            var defaultTokenCachePath = GetDefaultTokenCachePath();
            parts.Add($"TokenCacheStorePath={defaultTokenCachePath}");
            _logger.Verbose($"Using default token cache path: {defaultTokenCachePath}");
        }

        if (_config.RequireNewInstance)
        {
            parts.Add("RequireNewInstance=true");
        }

        return string.Join(";", parts);
    }

    private string ResolveLoginPrompt(bool hasExplicitCredentials)
    {
        if (!string.IsNullOrWhiteSpace(_config.LoginPrompt))
        {
            var configuredPrompt = _config.LoginPrompt!.Trim();
            if (
                configuredPrompt.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                || configuredPrompt.Equals("Never", StringComparison.OrdinalIgnoreCase)
                || configuredPrompt.Equals("Always", StringComparison.OrdinalIgnoreCase)
            )
            {
                return configuredPrompt;
            }

            _logger.Warning(
                $"Unsupported OAuth login prompt '{configuredPrompt}'. Falling back to default behavior."
            );
        }

        return hasExplicitCredentials ? "Never" : "Auto";
    }

    private static string GetDefaultTokenCachePath()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cacheDirectory = Path.Combine(homePath, ".xrmpackager");
        Directory.CreateDirectory(cacheDirectory);
        return Path.Combine(cacheDirectory, "TokenCache.dat");
    }

    /// <summary>
    /// Builds Client Secret connection string.
    /// </summary>
    private string BuildClientSecretConnectionString(string url)
    {
        if (string.IsNullOrEmpty(_config.AppId))
        {
            throw new InvalidOperationException(
                "AppId (Client ID) is required for ClientSecret authentication"
            );
        }

        if (string.IsNullOrEmpty(_config.ClientSecret))
        {
            throw new InvalidOperationException(
                "ClientSecret is required for ClientSecret authentication"
            );
        }

        var parts = new List<string>
        {
            $"AuthType=ClientSecret",
            $"Url={url}",
            $"ClientId={_config.AppId}",
            $"ClientSecret={_config.ClientSecret}",
        };

        if (_config.RequireNewInstance)
        {
            parts.Add("RequireNewInstance=true");
        }

        return string.Join(";", parts);
    }

    /// <summary>
    /// Builds Certificate connection string.
    /// </summary>
    private string BuildCertificateConnectionString(string url)
    {
        if (string.IsNullOrEmpty(_config.AppId))
        {
            throw new InvalidOperationException(
                "AppId (Client ID) is required for Certificate authentication"
            );
        }

        if (string.IsNullOrEmpty(_config.CertificateThumbprint))
        {
            throw new InvalidOperationException(
                "Certificate Thumbprint is required for Certificate authentication"
            );
        }

        var parts = new List<string>
        {
            $"AuthType=Certificate",
            $"Url={url}",
            $"ClientId={_config.AppId}",
            $"Thumbprint={_config.CertificateThumbprint}",
        };

        if (_config.RequireNewInstance)
        {
            parts.Add("RequireNewInstance=true");
        }

        return string.Join(";", parts);
    }
}

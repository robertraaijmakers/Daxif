namespace XrmPackager.Core.Authentication;

/// <summary>
/// Defines the authentication method to use when connecting to Dataverse.
/// </summary>
public enum AuthenticationMethod
{
    /// <summary>OAuth with username/password or interactive browser login</summary>
    OAuth,

    /// <summary>Client Secret (Service Principal) authentication</summary>
    ClientSecret,

    /// <summary>Certificate-based authentication</summary>
    Certificate,

    /// <summary>Connection string authentication</summary>
    ConnectionString,
}

/// <summary>
/// Configuration for authenticating with Dataverse.
/// </summary>
public class DataverseAuthConfig
{
    /// <summary>
    /// The Dataverse organization URL (e.g., https://org.crm.dynamics.com)
    /// </summary>
    public Uri OrganizationUrl { get; set; } = null!;

    /// <summary>
    /// The authentication method to use
    /// </summary>
    public AuthenticationMethod AuthMethod { get; set; } = AuthenticationMethod.OAuth;

    /// <summary>
    /// Username for OAuth authentication
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for OAuth authentication
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Application/Client ID for OAuth or ClientSecret authentication
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Client Secret for ClientSecret authentication
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Certificate Thumbprint for Certificate authentication
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    /// Redirect URI for OAuth (default: http://localhost)
    /// </summary>
    public string RedirectUri { get; set; } = "http://localhost";

    /// <summary>
    /// Login prompt behavior for OAuth (Auto, Never, Always).
    /// </summary>
    public string? LoginPrompt { get; set; }

    /// <summary>
    /// Login hint for OAuth interactive sign-in.
    /// </summary>
    public string? LoginHint { get; set; }

    /// <summary>
    /// Connection string (for ConnectionString auth method)
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Path to token cache file (OAuth only)
    /// </summary>
    public string? TokenCachePath { get; set; }

    /// <summary>
    /// Connection timeout (default: 2 minutes)
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether to require a new instance of the ServiceClient
    /// </summary>
    public bool RequireNewInstance { get; set; } = false;

    /// <summary>
    /// Log level for authentication diagnostics
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Info;
}

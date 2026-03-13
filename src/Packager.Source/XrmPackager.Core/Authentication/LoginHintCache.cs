namespace XrmPackager.Core.Authentication;

using System.Text.Json;

internal static class LoginHintCache
{
    private const string CachePathEnvironmentVariable = "DATAVERSE_LOGIN_HINT_CACHE_PATH";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class CacheDocument
    {
        public Dictionary<string, string> Hints { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public static string? TryGet(Uri organizationUrl)
    {
        try
        {
            var cacheFilePath = GetCacheFilePath();
            if (!File.Exists(cacheFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(cacheFilePath);
            var document = JsonSerializer.Deserialize<CacheDocument>(json);
            if (document?.Hints == null)
            {
                return null;
            }

            return document.Hints.TryGetValue(GetKey(organizationUrl), out var loginHint)
                ? loginHint
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Set(Uri organizationUrl, string loginHint)
    {
        if (string.IsNullOrWhiteSpace(loginHint))
        {
            return;
        }

        try
        {
            var cacheFilePath = GetCacheFilePath();
            var document = LoadDocument();
            document.Hints[GetKey(organizationUrl)] = loginHint.Trim();

            var directory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(document, JsonOptions);
            File.WriteAllText(cacheFilePath, json);
        }
        catch
        {
            // Best effort cache write only.
        }
    }

    private static CacheDocument LoadDocument()
    {
        var cacheFilePath = GetCacheFilePath();
        if (!File.Exists(cacheFilePath))
        {
            return new CacheDocument();
        }

        try
        {
            var json = File.ReadAllText(cacheFilePath);
            return JsonSerializer.Deserialize<CacheDocument>(json) ?? new CacheDocument();
        }
        catch
        {
            return new CacheDocument();
        }
    }

    private static string GetCacheFilePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(CachePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".xrmpackager",
            "login-hints.json"
        );
    }

    private static string GetKey(Uri organizationUrl)
    {
        return organizationUrl.GetLeftPart(UriPartial.Authority).TrimEnd('/').ToLowerInvariant();
    }
}

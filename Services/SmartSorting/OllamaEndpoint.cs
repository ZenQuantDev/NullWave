using System;

namespace NullWave.Services.SmartSorting;

/// <summary>
/// Turns whatever is in the OLLAMA_HOST environment variable into a URL NullWave can call.
///
/// Why it exists:
///   - OLLAMA_HOST is normally written as "host:port" or just "0.0.0.0", without "http://".
///     Using it unchanged produces an invalid address.
///   - "0.0.0.0" means "listen on every interface"; it is not an address you can connect to.
///   - On Windows, connecting to "localhost" tries the IPv6 loopback first, and when Ollama is not
///     running the failure can take seconds to arrive. "127.0.0.1" fails immediately.
///   - A host with no port means Ollama's default port, 11434.
/// </summary>
public static class OllamaEndpoint
{
    public const string Default = "http://127.0.0.1:11434";
    private const int DefaultPort = 11434;

    public static string Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return Default;

        var text = host.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return Default;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return Default;
        if (string.IsNullOrEmpty(uri.Host)) return Default;

        var hostName = uri.Host;
        if (hostName is "0.0.0.0" or "[::]" or "localhost" || hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            hostName = "127.0.0.1";

        // No port written: Ollama's own default, unless the address is a web address (https, or a
        // path such as http://proxy/ollama), where the normal web port is what the person meant.
        var authority = AuthorityOf(text);
        var hasPath = uri.AbsolutePath.Length > 1;
        var port = HasExplicitPort(authority) || uri.Scheme == Uri.UriSchemeHttps || hasPath
            ? uri.Port
            : DefaultPort;

        var isDefaultPortForScheme = (uri.Scheme == Uri.UriSchemeHttp && port == 80)
                                     || (uri.Scheme == Uri.UriSchemeHttps && port == 443);

        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme}://{hostName}{(isDefaultPortForScheme ? string.Empty : ":" + port)}{path}";
    }

    private static string AuthorityOf(string urlWithScheme)
    {
        var start = urlWithScheme.IndexOf("://", StringComparison.Ordinal) + 3;
        var end = urlWithScheme.IndexOfAny(new[] { '/', '?', '#' }, start);
        return end < 0 ? urlWithScheme[start..] : urlWithScheme[start..end];
    }

    private static bool HasExplicitPort(string authority)
    {
        var closeBracket = authority.LastIndexOf(']');
        var colon = authority.LastIndexOf(':');
        return colon >= 0 && colon > closeBracket && colon < authority.Length - 1;
    }
}
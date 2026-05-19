namespace Hermes.WpGallery;

public static class WpGalleryEndpoints
{
    public const string MessagePath = "/wp-json/hermes/v1/message";
    public const string ImagePath = "/wp-json/hermes/v1/image";
    public const string StreamPath = "/wp-json/hermes/v1/stream";
    public const string StatusPath = "/wp-json/hermes/v1/status";

    /// <summary>Normalizes site base URL (scheme + host).</summary>
    public static bool TryNormalizeSiteUrl(string? input, out string siteUrl, out string? error)
    {
        siteUrl = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "URL сайта не задан";
            return false;
        }

        input = input.Trim().TrimEnd('/');

        if (input.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ws = new Uri(input);
                siteUrl = ws.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase)
                    ? $"https://{ws.Host}"
                    : $"http://{ws.Host}";
                return true;
            }
            catch
            {
                error = "Некорректный WebSocket URL. Укажите адрес сайта, например: https://example.com";
                return false;
            }
        }

        if (input.Contains("/wp-json/", StringComparison.OrdinalIgnoreCase))
        {
            var idx = input.IndexOf("/wp-json/", StringComparison.OrdinalIgnoreCase);
            input = input[..idx];
        }

        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            input = "https://" + input;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            error = "Некорректный URL сайта";
            return false;
        }

        siteUrl = $"{uri.Scheme}://{uri.Host}";
        return true;
    }

    public static string BuildMessageUrl(string siteOrEndpoint)
    {
        if (siteOrEndpoint.Contains("/wp-json/hermes/v1/message", StringComparison.OrdinalIgnoreCase))
        {
            return siteOrEndpoint.Trim().TrimEnd('/');
        }

        return TryNormalizeSiteUrl(siteOrEndpoint, out var site, out _)
            ? $"{site}{MessagePath}"
            : string.Empty;
    }

    public static string BuildImageUrl(string siteOrEndpoint)
    {
        if (siteOrEndpoint.Contains("/wp-json/hermes/v1/image", StringComparison.OrdinalIgnoreCase))
        {
            return siteOrEndpoint.Trim().TrimEnd('/');
        }

        return TryNormalizeSiteUrl(siteOrEndpoint, out var site, out _)
            ? $"{site}{ImagePath}"
            : string.Empty;
    }

    public static string BuildStatusUrl(string siteOrEndpoint)
    {
        if (TryNormalizeSiteUrl(siteOrEndpoint, out var site, out _))
        {
            return $"{site}{StatusPath}";
        }

        if (siteOrEndpoint.Contains("/wp-json/hermes/v1/", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = siteOrEndpoint[..siteOrEndpoint.IndexOf("/wp-json/", StringComparison.OrdinalIgnoreCase)];
            return $"{baseUrl.TrimEnd('/')}{StatusPath}";
        }

        return string.Empty;
    }

    /// <summary>sender → channel в галерее и SSE (?channel=).</summary>
    public static string EffectiveSender(string? senderOrChannel)
    {
        var name = (senderOrChannel ?? "").Trim();
        if (!string.IsNullOrEmpty(name) && !name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return Environment.MachineName;
    }

    public static string EffectiveChannel(string? channel) => EffectiveSender(channel);
}

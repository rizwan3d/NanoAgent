namespace StemCode.Domain.Services;

internal static class CompatibleProviderBaseUrlNormalizer
{
    public static string Normalize(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("Base URL must be an absolute URL.", nameof(baseUrl));
        }

        return Normalize(uri);
    }

    public static string Normalize(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Base URL must use http or https.", nameof(uri));
        }

        UriBuilder builder = new(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            Path = NormalizePath(uri.AbsolutePath)
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static string NormalizePath(string path)
    {
        string normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "/"
            : path.Trim();

        if (normalizedPath == "/")
        {
            return "/v1";
        }

        return normalizedPath.TrimEnd('/');
    }
}

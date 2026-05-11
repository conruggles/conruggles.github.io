using conruggles.github.io.Models;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace conruggles.github.io.Services;

public sealed class SiteContentRepository
{
    private static readonly Regex H1Regex = new(
        @"<h1\b[^>]*>(?<title>.*?)</h1>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private readonly HttpClient http;
    private IReadOnlyList<SitePage>? cachedPages;

    public SiteContentRepository(HttpClient http)
    {
        this.http = http;
    }

    public async Task<IReadOnlyList<SitePage>> GetAllPagesAsync()
    {
        if (cachedPages is not null)
            return cachedPages;

        string manifest;
        try
        {
            manifest = await http.GetStringAsync("content/pages-manifest.txt");
        }
        catch (HttpRequestException)
        {
            cachedPages = [];
            return cachedPages;
        }

        var draftPages = manifest
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Replace('\\', '/'))
            .Where(line => line.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(relativePath => new SitePage
            {
                RelativePath = relativePath,
                Title = PathPartToTitle(Path.GetFileNameWithoutExtension(relativePath))
            })
            .ToList();

        var pages = await Task.WhenAll(draftPages.Select(async page =>
        {
            try
            {
                var html = await http.GetStringAsync(page.ContentPath);
                var titleFromH1 = ExtractTitleFromHtml(html);
                if (string.IsNullOrWhiteSpace(titleFromH1))
                    return page;

                return new SitePage
                {
                    Title = titleFromH1,
                    RelativePath = page.RelativePath
                };
            }
            catch (HttpRequestException)
            {
                return page;
            }
        }));

        cachedPages = pages
            .OrderBy(page => page.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return cachedPages;
    }

    public async Task<SitePage?> GetDefaultPageAsync()
    {
        var pages = await GetAllPagesAsync();
        return pages.FirstOrDefault();
    }

    public async Task<SitePage?> FindAsync(string relativePath)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return null;

        var pages = await GetAllPagesAsync();
        return pages.FirstOrDefault(page => string.Equals(page.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    public static string PathPartToTitle(string pathPart)
    {
        var normalized = pathPart
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return "Untitled";

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        var normalized = Uri.UnescapeDataString(relativePath)
            .Split('?', '#')[0]
            .Trim('/');

        if (normalized.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["content/".Length..];

        return normalized.Replace('\\', '/');
    }

    private static string? ExtractTitleFromHtml(string html)
    {
        var match = H1Regex.Match(html);
        if (!match.Success)
            return null;

        var raw = match.Groups["title"].Value;
        var stripped = HtmlTagRegex.Replace(raw, string.Empty);
        var decoded = WebUtility.HtmlDecode(stripped).Trim();
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }
}

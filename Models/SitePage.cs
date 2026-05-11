namespace conruggles.github.io.Models;

public sealed class SitePage
{
    public required string Title { get; init; }
    public required string RelativePath { get; init; }

    public string ContentPath => $"content/{RelativePath}";
    public string RoutePath => $"/{RelativePath}";
}

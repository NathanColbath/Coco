namespace ComicStudio.Api.NovelAi;

/// <summary>
/// NovelAI's hosts. The documentation directs third-party API users to the
/// generation hosts, and warns against using the primary (frontend) API beyond
/// the <c>/ai/</c> routes — so account lookups belong on the image host, not on
/// <c>api.novelai.net</c>.
/// </summary>
public static class NovelAiEndpoints
{
    /// <summary>Image generation, and the account/subscription lookup.</summary>
    public const string Image = "https://image.novelai.net";

    /// <summary>Text generation (the OpenAI-compatible <c>/oa/v1</c> endpoints).</summary>
    public const string Text = "https://text.novelai.net";

    /// <summary>The primary API. Kept only as a fallback for account lookups.</summary>
    public const string Primary = "https://api.novelai.net";

    public static string Normalize(string baseUrl) =>
        baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
}

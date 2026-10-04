using System.Text.Json.Serialization;

namespace ComicStudio.Api.Domain;

/// <summary>
/// How explicit a project is allowed to be. Drives prompt tags, the negatives
/// that are applied or stripped, and how candidly the curator is asked to
/// describe a scene.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentRating>))]
public enum ContentRating
{
    /// <summary>Nothing adult. Safety tags are added to every request.</summary>
    General = 0,

    /// <summary>Suggestive and adult themes permitted, without explicit acts.</summary>
    Mature = 1,

    /// <summary>No content filter. Censoring tags are actively excluded.</summary>
    Explicit = 2,
}

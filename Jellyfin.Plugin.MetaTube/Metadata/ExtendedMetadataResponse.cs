using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MetaTube.Metadata;

public class ExtendedMetadataResponse
{
    [JsonPropertyName("provider")] public string Provider { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("number")] public string Number { get; set; }
    [JsonPropertyName("ratings")] public MovieRating[] Ratings { get; set; } = Array.Empty<MovieRating>();
    [JsonPropertyName("tags")] public MovieTag[] Tags { get; set; } = Array.Empty<MovieTag>();
    [JsonPropertyName("reviews")] public MovieReviewPage Reviews { get; set; }
    [JsonPropertyName("review_status")] public string ReviewStatus { get; set; }
    [JsonPropertyName("metadata_status")] public string MetadataStatus { get; set; }
    [JsonPropertyName("review_provider")] public string ReviewProvider { get; set; }
    [JsonPropertyName("javdb_status")] public string JavdbStatus { get; set; }
}

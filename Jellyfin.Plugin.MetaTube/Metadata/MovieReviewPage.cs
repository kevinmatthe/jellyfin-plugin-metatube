using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MetaTube.Metadata;

public class MovieReviewPage
{
    [JsonPropertyName("items")] public MovieReview[] Items { get; set; } = Array.Empty<MovieReview>();
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("has_more")] public bool HasMore { get; set; }
    [JsonPropertyName("total")] public long? Total { get; set; }
    [JsonPropertyName("paginated")] public bool Paginated { get; set; }
    [JsonPropertyName("fetched_at")] public DateTimeOffset? FetchedAt { get; set; }
    [JsonPropertyName("cached")] public bool Cached { get; set; }
}

public class MovieReview
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; }
    [JsonPropertyName("author")] public string Author { get; set; }
    [JsonPropertyName("comment")] public string Comment { get; set; }
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("scale_max")] public double ScaleMax { get; set; }
    [JsonPropertyName("helpful_count")] public long? HelpfulCount { get; set; }
    [JsonPropertyName("date")] public DateTimeOffset? Date { get; set; }
}

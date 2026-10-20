using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MetaTube.Metadata;

public class MovieRating
{
    [JsonPropertyName("source")] public string Source { get; set; }
    [JsonPropertyName("source_id")] public string SourceId { get; set; }
    [JsonPropertyName("value")] public double Value { get; set; }
    [JsonPropertyName("scale_max")] public double ScaleMax { get; set; }
    [JsonPropertyName("vote_count")] public long? VoteCount { get; set; }
    [JsonPropertyName("review_count")] public long? ReviewCount { get; set; }
    [JsonPropertyName("distribution")] public RatingBucket[] Distribution { get; set; } = Array.Empty<RatingBucket>();
    [JsonPropertyName("fetched_at")] public DateTimeOffset? FetchedAt { get; set; }
}

public class RatingBucket
{
    [JsonPropertyName("value")] public double Value { get; set; }
    [JsonPropertyName("count")] public long Count { get; set; }
}

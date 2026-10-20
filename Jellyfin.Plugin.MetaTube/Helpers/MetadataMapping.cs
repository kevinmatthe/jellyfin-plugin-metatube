using Jellyfin.Plugin.MetaTube.Metadata;

namespace Jellyfin.Plugin.MetaTube.Helpers;

public static class MetadataMapping
{
    public static bool IsValidRating(MovieRating rating) => rating != null &&
        double.IsFinite(rating.Value) && double.IsFinite(rating.ScaleMax) &&
        rating.ScaleMax > 0 && rating.Value >= 0 && rating.Value <= rating.ScaleMax &&
        (rating.Value > 0 || rating.VoteCount > 0);

    public static float? GetCommunityRating(Metadata.MovieInfo movie)
    {
        var rating = movie.Ratings?.FirstOrDefault(IsValidRating);
        if (rating != null)
            return (float)Math.Round(rating.Value / rating.ScaleMax * 10, 1);
        // The legacy SDK contract uses five-point scores. Never emit a value above ten.
        return float.IsFinite(movie.Score) && movie.Score > 0 && movie.Score <= 5
            ? (float)Math.Round(movie.Score * 2, 1) : null;
    }

    public static IEnumerable<string> GetNativeTags(Metadata.MovieInfo movie) =>
        (movie.Tags ?? Array.Empty<MovieTag>())
        .Where(tag => tag != null && !string.Equals(tag.Category, "genre", StringComparison.OrdinalIgnoreCase))
        .Select(tag => tag.Name)
        .Concat(new[] { movie.Series, movie.Maker, movie.Label })
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name.Trim().Replace("|", " / "))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public static string BuildBackendPath(string path, string provider, string id) =>
        path.TrimEnd('/') + "/" + Uri.EscapeDataString(provider) + "/" + Uri.EscapeDataString(id);
}

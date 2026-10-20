#if !__EMBY__
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MetaTube.Helpers;
using Jellyfin.Plugin.MetaTube.Metadata;

namespace Jellyfin.Plugin.MetaTube.Api;

public static class JavdbEnrichment
{
    public static async Task ApplyAsync(IMetadataBackend backend, ExtendedMetadataResponse result,
        int offset, int limit, bool refresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        result.ReviewProvider = "JAVDB";
        result.ReviewStatus = "unavailable";
        if (string.IsNullOrWhiteSpace(result.Number) ||
            !Regex.IsMatch(result.Number, @"\A[A-Za-z0-9][A-Za-z0-9._-]{1,79}\z", RegexOptions.CultureInvariant))
        {
            result.JavdbStatus = "number_unavailable";
            return;
        }
        try
        {
            // The backend saves the strict number -> internal source ID association.
            // Its URL pager then reads comments by that ID without repeating a search.
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var info = await backend.GetMovieAsync("JAVDB", result.Number, timeout.Token).ConfigureAwait(false);
                result.Ratings = (result.Ratings ?? Array.Empty<MovieRating>())
                    .Concat((info.Ratings ?? Array.Empty<MovieRating>()).Where(rating =>
                        MetadataMapping.IsValidRating(rating) && string.Equals(rating.Source, "JAVDB", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                result.Reviews = await backend.GetReviewsAsync("JAVDB", result.Number, offset, limit, !refresh,
                    timeout.Token).ConfigureAwait(false);
            }
            result.ReviewStatus = "available";
            result.JavdbStatus = "available";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.JavdbStatus = "unavailable";
        }
        catch (HttpRequestException error)
        {
            result.JavdbStatus = StatusFor(error.StatusCode);
        }
        catch (JsonException)
        {
            result.JavdbStatus = "unavailable";
        }
    }

    internal static string StatusFor(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.NotFound => "not_found",
        HttpStatusCode.TooManyRequests => "rate_limited",
        HttpStatusCode.Unauthorized => "authentication_required",
        HttpStatusCode.Forbidden => "blocked",
        _ => "unavailable"
    };
}

public interface IExtendedMetadataSettings
{
    bool PanelEnabled { get; }
    bool JavdbEnabled { get; }
}

public sealed class PluginMetadataSettings : IExtendedMetadataSettings
{
    public bool PanelEnabled => Plugin.Instance?.Configuration.EnableExtendedMetadata != false;
    public bool JavdbEnabled => Plugin.Instance?.Configuration.EnableJavdbMetadata == true;
}
#endif

#if !__EMBY__
using Jellyfin.Plugin.MetaTube.Metadata;

namespace Jellyfin.Plugin.MetaTube.Api;

public interface IMetadataBackend
{
    Task<MovieInfo> GetMovieAsync(string provider, string id, CancellationToken cancellationToken);
    Task<MovieReviewPage> GetReviewsAsync(string provider, string id, int offset, int limit, bool lazy,
        CancellationToken cancellationToken);
}

public class MetadataBackend : IMetadataBackend
{
    public Task<MovieInfo> GetMovieAsync(string provider, string id, CancellationToken cancellationToken) =>
        ApiClient.GetMovieInfoAsync(provider, id, cancellationToken);

    public Task<MovieReviewPage> GetReviewsAsync(string provider, string id, int offset, int limit, bool lazy,
        CancellationToken cancellationToken) =>
        ApiClient.GetMovieReviewPageAsync(provider, id, offset, limit, lazy, cancellationToken);
}
#endif

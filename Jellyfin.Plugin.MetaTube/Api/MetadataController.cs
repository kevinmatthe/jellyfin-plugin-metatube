#if !__EMBY__
using System.Net;
using Jellyfin.Plugin.MetaTube.Extensions;
using Jellyfin.Plugin.MetaTube.Helpers;
using Jellyfin.Plugin.MetaTube.Metadata;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MetaTube.Api;

[ApiController]
[Route("MetaTube")]
public class MetadataController : ControllerBase
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IMetadataBackend _backend;
    private readonly ILogger<MetadataController> _logger;
    private readonly IExtendedMetadataSettings _settings;

    public MetadataController(ILibraryManager library, IUserManager users, IMetadataBackend backend,
        ILogger<MetadataController> logger, IExtendedMetadataSettings settings = null)
    {
        _library = library;
        _users = users;
        _backend = backend;
        _logger = logger;
        _settings = settings ?? new PluginMetadataSettings();
    }

    [Authorize]
    [HttpGet("Items/{itemId:guid}/Metadata")]
    public async Task<ActionResult<ExtendedMetadataResponse>> GetMetadata(Guid itemId, int offset = 0,
        int limit = 10, bool refresh = false, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(User.FindFirst("Jellyfin.UserId")?.Value, out var userId) || userId == Guid.Empty)
            return Unauthorized();
        var user = _users.GetUserById(userId);
        if (user == null) return Unauthorized();
        if (offset < 0 || limit < 1 || limit > 50) return BadRequest("Invalid review pagination.");
        if (!_settings.PanelEnabled) return NotFound();

        // A non-null user is required for library membership and parental/tag restrictions.
        var item = _library.GetItemById<Movie>(itemId, user);
        if (item == null) return NotFound();
        var pid = item.GetPid(Plugin.ProviderId);
        if (string.IsNullOrWhiteSpace(pid.Provider) || string.IsNullOrWhiteSpace(pid.Id)) return NotFound();

        var result = new ExtendedMetadataResponse
        {
            Provider = pid.Provider, Id = pid.Id, ReviewProvider = pid.Provider,
            MetadataStatus = "available", ReviewStatus = "available"
        };
        var directJavdb = string.Equals(pid.Provider, "JAVDB", StringComparison.OrdinalIgnoreCase);
        // Source metadata and saved review pages have independent lifetimes and failures.
        if (_settings.JavdbEnabled && !directJavdb)
        {
            await ReadMovieAsync(result, cancellationToken).ConfigureAwait(false);
            await JavdbEnrichment.ApplyAsync(_backend, result, offset, limit, refresh, cancellationToken).ConfigureAwait(false);
        }
        else if (directJavdb)
        {
            result.JavdbStatus = "available";
            await ReadMovieAsync(result, cancellationToken).ConfigureAwait(false);
            await ReadReviewsAsync(result, offset, limit, !refresh, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Task.WhenAll(ReadMovieAsync(result, cancellationToken),
                ReadReviewsAsync(result, offset, limit, !refresh, cancellationToken)).ConfigureAwait(false);
        }
        return Ok(result);
    }

    private async Task ReadMovieAsync(ExtendedMetadataResponse result, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var info = await _backend.GetMovieAsync(result.Provider, result.Id, timeout.Token).ConfigureAwait(false);
            result.Number = info.Number;
            result.Ratings = (info.Ratings ?? Array.Empty<MovieRating>()).Where(MetadataMapping.IsValidRating).ToArray();
            result.Tags = info.Tags ?? Array.Empty<MovieTag>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.MetadataStatus = "unavailable";
            SetJavdbFailure(result, null);
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogWarning("MetaTube metadata unavailable for source {Provider}", result.Provider);
            result.MetadataStatus = "unavailable";
            SetJavdbFailure(result, (error as HttpRequestException)?.StatusCode);
        }
    }

    private async Task ReadReviewsAsync(ExtendedMetadataResponse result, int offset, int limit, bool lazy,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            result.Reviews = await _backend.GetReviewsAsync(result.Provider, result.Id, offset, limit, lazy,
                timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotImplemented)
        {
            result.ReviewStatus = "unsupported";
            SetJavdbFailure(result, error.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result.ReviewStatus = "unavailable";
            SetJavdbFailure(result, null);
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogWarning("MetaTube reviews unavailable for source {Provider}", result.Provider);
            result.ReviewStatus = "unavailable";
            SetJavdbFailure(result, (error as HttpRequestException)?.StatusCode);
        }
    }

    private static void SetJavdbFailure(ExtendedMetadataResponse result, HttpStatusCode? status)
    {
        if (string.Equals(result.Provider, "JAVDB", StringComparison.OrdinalIgnoreCase))
            result.JavdbStatus = JavdbEnrichment.StatusFor(status);
    }

    [AllowAnonymous]
    [HttpGet("metadata.js")]
    public IActionResult GetScript() => Asset("metadata.js", "application/javascript; charset=utf-8");

    [AllowAnonymous]
    [HttpGet("metadata.css")]
    public IActionResult GetStyles() => Asset("metadata.css", "text/css; charset=utf-8");

    private IActionResult Asset(string name, string contentType)
    {
        var stream = typeof(MetadataController).Assembly.GetManifestResourceStream(
            "Jellyfin.Plugin.MetaTube.Web." + name);
        if (stream == null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, contentType);
    }
}
#endif

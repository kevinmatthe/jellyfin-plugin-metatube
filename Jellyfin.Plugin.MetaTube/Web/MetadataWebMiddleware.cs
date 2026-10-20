#if !__EMBY__
using System.Net;
using System.Text;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.MetaTube.Web;

public class MetadataWebMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PathString _baseUrl;

    public MetadataWebMiddleware(RequestDelegate next, IServerConfigurationManager configuration = null)
    {
        _next = next;
        _baseUrl = new PathString(configuration?.GetNetworkConfiguration().BaseUrl ?? "");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestPath = context.Request.Path;
        var assetPathBase = context.Request.PathBase;
        // Startup filters run before Jellyfin maps its configured BaseUrl. Capture
        // that prefix now: Map restores PathBase before this middleware resumes.
        if (_baseUrl.HasValue && requestPath.StartsWithSegments(_baseUrl, out var matched, out var remaining))
        {
            requestPath = remaining;
            assetPathBase = assetPathBase.Add(matched);
        }
        var path = requestPath.Value?.TrimEnd('/');
        if (!HttpMethods.IsGet(context.Request.Method) ||
            path is not ("" or "/web" or "/index.html" or "/web/index.html") ||
            Plugin.Instance?.Configuration.EnableExtendedMetadata == false)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var original = context.Response.Body;
        var acceptEncoding = context.Request.Headers.AcceptEncoding;
        var etag = context.Request.Headers.IfNoneMatch;
        var modified = context.Request.Headers.IfModifiedSince;
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("If-None-Match");
        context.Request.Headers.Remove("If-Modified-Since");
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            if (context.Response.StatusCode == StatusCodes.Status200OK &&
                context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true &&
                !context.Response.Headers.ContainsKey("Content-Encoding"))
            {
                var html = Encoding.UTF8.GetString(bytes);
                var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (end >= 0 && !html.Contains("data-metatube-metadata", StringComparison.Ordinal))
                {
                    var prefix = WebUtility.HtmlEncode(assetPathBase.Value?.TrimEnd('/') ?? "");
                    var version = typeof(MetadataWebMiddleware).Assembly.GetName().Version?.ToString() ?? "1";
                    var assets = $"<link data-metatube-metadata rel=\"stylesheet\" href=\"{prefix}/MetaTube/metadata.css?v={version}\">" +
                        $"<script data-metatube-metadata defer src=\"{prefix}/MetaTube/metadata.js?v={version}\"></script>";
                    bytes = Encoding.UTF8.GetBytes(html.Insert(end, assets));
                    context.Response.Headers.Remove("ETag");
                    context.Response.Headers.Remove("Last-Modified");
                    context.Response.Headers.CacheControl = "private, no-cache";
                }
            }
            context.Response.ContentLength = bytes.Length;
            await original.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
            context.Request.Headers.AcceptEncoding = acceptEncoding;
            context.Request.Headers.IfNoneMatch = etag;
            context.Request.Headers.IfModifiedSince = modified;
        }
    }
}
#endif

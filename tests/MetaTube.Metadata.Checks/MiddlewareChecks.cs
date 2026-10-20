using System.IO.Compression;
using System.Reflection;
using System.Text;
using Jellyfin.Plugin.MetaTube.Web;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

public static class MiddlewareChecks
{
    private const string Shell = "<html><head></head><body>Jellyfin</body></html>";

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    public static async Task Run()
    {
        foreach (var prefix in new[] { "", "/jellyfin", "/media/jellyfin" })
        {
            foreach (var pathBase in new[] { "", "/proxy" })
            {
                var context = CreateContext(prefix + "/web/index.html", pathBase);
                context.Request.QueryString = new QueryString("?v=1&route=details");
                var headersSuppressed = false;
                var mappedPath = "";
                var mappedPathBase = "";
                await Pipeline(prefix, async ctx =>
                {
                    mappedPath = ctx.Request.Path;
                    mappedPathBase = ctx.Request.PathBase;
                    headersSuppressed = !ctx.Request.Headers.ContainsKey("Accept-Encoding") &&
                        !ctx.Request.Headers.ContainsKey("If-None-Match") &&
                        !ctx.Request.Headers.ContainsKey("If-Modified-Since");
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength = Encoding.UTF8.GetByteCount(Shell);
                    ctx.Response.Headers.ETag = "\"original-shell\"";
                    ctx.Response.Headers.LastModified = "Wed, 01 Oct 2025 00:00:00 GMT";
                    await ctx.Response.WriteAsync(Shell);
                })(context);
                var html = ReadBody(context);
                Check(html.Contains($"href=\"{pathBase}{prefix}/MetaTube/metadata.css?v=") &&
                    html.Contains($"src=\"{pathBase}{prefix}/MetaTube/metadata.js?v="),
                    $"startup filter injects assets before BaseUrl mapping ({pathBase}{prefix})");
                Check(mappedPath == "/web/index.html" && mappedPathBase == pathBase + prefix,
                    "shell runs inside the real mapped branch");
                Check(headersSuppressed, "shell rendering suppresses compression and conditional cache headers");
                Check(!context.Response.Headers.ContainsKey("ETag") &&
                    !context.Response.Headers.ContainsKey("Last-Modified") &&
                    context.Response.Headers.CacheControl == "private, no-cache" &&
                    context.Response.ContentLength == Encoding.UTF8.GetByteCount(html),
                    "injected shell gets correct length and fresh cache headers");
                Check(context.Request.PathBase == pathBase && context.Request.Path == prefix + "/web/index.html",
                    "mapped request paths are restored");
                CheckRestoredHeaders(context);
            }
        }

        foreach (var path in new[] { "/jellyfin", "/jellyfin/", "/jellyfin/web", "/jellyfin/web/", "/jellyfin/index.html" })
        {
            var context = CreateContext(path);
            await Pipeline("/jellyfin", WriteShell)(context);
            Check(ReadBody(context).Contains("src=\"/jellyfin/MetaTube/metadata.js?v="),
                "configured BaseUrl supports each exact web shell route: " + path);
        }

        // The middleware may also run after an upstream proxy has already populated PathBase.
        var alreadyMapped = CreateContext("/web/index.html", "/jellyfin");
        await Pipeline("/jellyfin", WriteShell)(alreadyMapped);
        Check(ReadBody(alreadyMapped).Contains("src=\"/jellyfin/MetaTube/metadata.js?v=") &&
            !ReadBody(alreadyMapped).Contains("/jellyfin/jellyfin/"),
            "existing PathBase does not duplicate the configured prefix");

        foreach (var path in new[] { "/jellyfin/Items", "/jellyfin/web/help.html", "/jellyfin/other/index.html",
            "/jellyfin-extra/web/index.html", "/elsewhere/web/index.html", "/jellyfin/web/index.html/extra" })
        {
            var context = CreateContext(path);
            var headersPreserved = false;
            await Pipeline("/jellyfin", async ctx =>
            {
                headersPreserved = ctx.Request.Headers.AcceptEncoding == "gzip, br";
                await WriteShell(ctx);
            })(context);
            Check(ReadBody(context) == Shell && headersPreserved,
                "non-shell HTML remains unchanged: " + path);
        }

        var duplicate = CreateContext("/jellyfin/web/index.html");
        var existingShell = Shell.Replace("</body>", "<script data-metatube-metadata src=\"existing.js\"></script></body>");
        await Pipeline("/jellyfin", async ctx =>
        {
            ctx.Response.ContentType = "text/html";
            ctx.Response.Headers.ETag = "\"existing\"";
            await ctx.Response.WriteAsync(existingShell);
        })(duplicate);
        Check(ReadBody(duplicate) == existingShell && duplicate.Response.Headers.ETag == "\"existing\"",
            "existing asset marker avoids duplicate injection and cache changes");

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(Shell));
        var compressedBytes = compressed.ToArray();
        var encoded = CreateContext("/jellyfin/web/index.html");
        await Pipeline("/jellyfin", async ctx =>
        {
            ctx.Response.ContentType = "text/html";
            ctx.Response.Headers.ContentEncoding = "gzip";
            await ctx.Response.Body.WriteAsync(compressedBytes);
        })(encoded);
        Check(((MemoryStream)encoded.Response.Body).ToArray().SequenceEqual(compressedBytes) &&
            encoded.Response.Headers.ContentEncoding == "gzip" && encoded.Response.ContentLength == compressedBytes.Length,
            "unexpected compressed shell bytes remain intact");
        CheckRestoredHeaders(encoded);

        var failure = CreateContext("/jellyfin/web/index.html");
        var originalBody = failure.Response.Body;
        var expectedFailure = new InvalidOperationException("downstream failed");
        try
        {
            await Pipeline("/jellyfin", _ => throw expectedFailure)(failure);
            throw new Exception("downstream exception should propagate");
        }
        catch (InvalidOperationException ex) when (ReferenceEquals(ex, expectedFailure))
        {
            Check(ReferenceEquals(failure.Response.Body, originalBody), "downstream failure restores original response body");
            CheckRestoredHeaders(failure);
        }

        var post = CreateContext("/jellyfin/web/index.html");
        post.Request.Method = "POST";
        await Pipeline("/jellyfin", WriteShell)(post);
        Check(ReadBody(post) == Shell, "non-GET shell responses remain unchanged");

        var notFound = CreateContext("/jellyfin/web/index.html");
        await Pipeline("/jellyfin", async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteShell(ctx);
        })(notFound);
        Check(ReadBody(notFound) == Shell, "non-success shell responses remain unchanged");
    }

    private static DefaultHttpContext CreateContext(string path, string pathBase = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.PathBase = pathBase;
        context.Request.Headers.AcceptEncoding = "gzip, br";
        context.Request.Headers.IfNoneMatch = "\"original-shell\"";
        context.Request.Headers.IfModifiedSince = "Wed, 01 Oct 2025 00:00:00 GMT";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RequestDelegate Pipeline(string baseUrl, RequestDelegate terminal)
    {
        var configuration = DispatchProxy.Create<IServerConfigurationManager, ConfigurationStub>();
        ((ConfigurationStub)(object)configuration).Network = new NetworkConfiguration { BaseUrl = baseUrl };
        var services = new ServiceCollection().AddSingleton(configuration).BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        new MetadataStartupFilter().Configure(builder =>
        {
            if (!string.IsNullOrEmpty(baseUrl))
                builder.Map(baseUrl, branch => branch.Run(terminal));
            builder.Run(terminal);
        })(app);
        return app.Build();
    }

    private static Task WriteShell(HttpContext context)
    {
        context.Response.ContentType = "text/html";
        return context.Response.WriteAsync(Shell);
    }

    private static string ReadBody(HttpContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static void CheckRestoredHeaders(HttpContext context) =>
        Check(context.Request.Headers.AcceptEncoding == "gzip, br" &&
            context.Request.Headers.IfNoneMatch == "\"original-shell\"" &&
            context.Request.Headers.IfModifiedSince == "Wed, 01 Oct 2025 00:00:00 GMT",
            "original request compression and cache headers are restored");

    public class ConfigurationStub : DispatchProxy
    {
        public NetworkConfiguration Network { get; set; }

        protected override object Invoke(MethodInfo method, object[] args) =>
            method.Name == "GetConfiguration" && (string)args[0] == NetworkConfigurationStore.StoreKey
                ? Network
                : throw new InvalidOperationException("Unexpected configuration call: " + method.Name);
    }
}

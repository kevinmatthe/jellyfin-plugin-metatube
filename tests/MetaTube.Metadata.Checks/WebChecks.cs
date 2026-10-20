using System.Security.Claims;
using System.Reflection;
using System.Text;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MetaTube.Api;
using Jellyfin.Plugin.MetaTube.Metadata;
using Jellyfin.Plugin.MetaTube.Web;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

public static class WebChecks
{
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    public static async Task Run()
    {
        var backend = new Backend();
        var users = DispatchProxy.Create<IUserManager, Stub>();
        var library = DispatchProxy.Create<ILibraryManager, Stub>();
        var userId = Guid.NewGuid();
        var user = new User("reader", "auth", "reset");
        var movie = new Movie();
        movie.SetProviderId("MetaTube", "Example:sample");
        ((Stub)(object)users).Call = (method, args) => method.Name == "GetUserById" && (Guid)args[0] == userId ? user : null;
        var visible = false;
        ((Stub)(object)library).Call = (method, args) =>
        {
            Check(method.Name == "GetItemById" && method.IsGenericMethod && args.Length == 2 && ReferenceEquals(args[1], user), "library lookup enforces current user's visibility");
            return visible ? movie : null;
        };
        var controller = new MetadataController(library, users, backend, NullLogger<MetadataController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        Check((await controller.GetMetadata(Guid.NewGuid())).Result is UnauthorizedResult && backend.Calls == 0, "anonymous requests never call backend");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("Jellyfin.UserId",Guid.NewGuid().ToString())},"test"));
        Check((await controller.GetMetadata(Guid.NewGuid())).Result is UnauthorizedResult && backend.Calls == 0, "unknown user cannot trigger an unrestricted library lookup");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("Jellyfin.UserId",Guid.Empty.ToString())},"test"));
        Check((await controller.GetMetadata(Guid.NewGuid())).Result is UnauthorizedResult && backend.Calls == 0, "empty user ID cannot trigger an unrestricted library lookup");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("Jellyfin.UserId",userId.ToString())},"test"));
        Check((await controller.GetMetadata(Guid.NewGuid())).Result is NotFoundResult && backend.Calls == 0, "inaccessible items never disclose cached metadata");
        visible = true;
        movie.SetProviderId("MetaTube", "invalid-without-source");
        Check((await controller.GetMetadata(Guid.NewGuid())).Result is NotFoundResult && backend.Calls == 0, "malformed MetaTube IDs never call backend");
        movie.SetProviderId("MetaTube", "Example:sample");
        var data = (ExtendedMetadataResponse)((OkObjectResult)(await controller.GetMetadata(Guid.NewGuid())).Result).Value;
        Check(data.Reviews.Items.Length == 1 && backend.ReviewCalls == 1 && backend.LastLazy, "item page automatically reads cached-or-fetched first review page");
        await controller.GetMetadata(Guid.NewGuid(), refresh: true);
        Check(!backend.LastLazy, "explicit refresh bypasses persisted review cache");
        Check((await controller.GetMetadata(Guid.NewGuid(), offset: -1)).Result is BadRequestObjectResult, "negative pagination rejected");
        backend.FailReviews = true;
        data = (ExtendedMetadataResponse)((OkObjectResult)(await controller.GetMetadata(Guid.NewGuid())).Result).Value;
        Check(data.ReviewStatus == "unavailable" && data.Provider == "Example", "review failure leaves metadata usable");
        backend.FailReviews = false;
        backend.FailMovie = true;
        data = (ExtendedMetadataResponse)((OkObjectResult)(await controller.GetMetadata(Guid.NewGuid())).Result).Value;
        Check(data.MetadataStatus == "unavailable" && data.Reviews.Items.Length == 1,
            "metadata source failure does not hide saved review pages");

        foreach (var prefix in new[]{"", "/jellyfin"})
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/web/index.html";
            context.Request.PathBase = prefix;
            context.Response.Body = new MemoryStream();
            var middleware = new MetadataWebMiddleware(async ctx =>
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.Headers.ETag = "old-shell";
                await ctx.Response.WriteAsync("<html><head></head><body>Jellyfin</body></html>");
            });
            await middleware.InvokeAsync(context);
            var html = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
            Check(html.Contains(prefix+"/MetaTube/metadata.js?v=") && html.Contains(prefix+"/MetaTube/metadata.css?v="), "HTML shell receives versioned assets with PathBase");
            Check(!context.Response.Headers.ContainsKey("ETag"), "modified shell does not reuse original ETag");
        }
        var other = new DefaultHttpContext();
        other.Request.Method = "GET";
        other.Request.Path = "/Items";
        other.Response.Body = new MemoryStream();
        await new MetadataWebMiddleware(ctx => ctx.Response.WriteAsync("{}" )).InvokeAsync(other);
        Check(Encoding.UTF8.GetString(((MemoryStream)other.Response.Body).ToArray()) == "{}", "non-shell responses remain unchanged");
        await MiddlewareChecks.Run();
    }

    public class Stub : DispatchProxy
    {
        public Func<MethodInfo, object[], object> Call { get; set; }
        protected override object Invoke(MethodInfo method, object[] args) => Call(method,args);
    }

    class Backend : IMetadataBackend
    {
        public int Calls;
        public int ReviewCalls;
        public bool LastLazy;
        public bool FailReviews;
        public bool FailMovie;
        public Task<MovieInfo> GetMovieAsync(string provider, string id, CancellationToken ct)
        {
            Calls++;
            if (FailMovie) throw new HttpRequestException("metadata source unavailable");
            return Task.FromResult(new MovieInfo { Provider = provider, Id = id });
        }
        public Task<MovieReviewPage> GetReviewsAsync(string provider,string id,int offset,int limit,bool lazy,CancellationToken ct)
        {
            ReviewCalls++;
            LastLazy=lazy;
            if (FailReviews) throw new HttpRequestException("upstream failure");
            return Task.FromResult(new MovieReviewPage { Items = new[]{ new MovieReview { Comment="Example" } } });
        }
    }
}

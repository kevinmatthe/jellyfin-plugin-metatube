using System.Net;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.MetaTube.Api;
using Jellyfin.Plugin.MetaTube.Metadata;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

public static class JavdbChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    static ExtendedMetadataResponse Primary(string number = "ABC-123") => new()
    {
        Provider = "Fixture", Id = "original", Number = number,
        Ratings = new[] { new MovieRating { Source = "Fixture", Value = 4, ScaleMax = 5 } },
        MetadataStatus = "available"
    };

    public static async Task Run()
    {
        var backend = new Backend();
        var result = Primary();
        await JavdbEnrichment.ApplyAsync(backend, result, 0, 10, false, CancellationToken.None);
        Check(result.Ratings.Length == 2 && result.Ratings[0].Source == "Fixture" &&
            result.Ratings[1].Source == "JAVDB" && result.Ratings[1].Value == 4.1 && result.Ratings[1].ScaleMax == 5,
            "JAVDB supplements original ratings with its five-point scale");
        Check(result.ReviewProvider == "JAVDB" && result.JavdbStatus == "available" &&
            result.Reviews.Items[0].Source == "JAVDB", "selected comments carry explicit JAVDB attribution");
        Check(backend.MovieId == "ABC-123" && backend.ReviewId == "ABC-123" && backend.Lazy,
            "JAVDB lookup uses movie number and persisted page cache");
        await JavdbEnrichment.ApplyAsync(backend, Primary(), 10, 10, true, CancellationToken.None);
        Check(backend.Offset == 10 && !backend.Lazy, "JAVDB explicit page refresh forwards the requested offset");

        foreach (var status in new[] {HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests,
            HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.BadGateway})
        {
            backend = new Backend { Failure = status };
            result = Primary();
            await JavdbEnrichment.ApplyAsync(backend, result, 0, 10, false, CancellationToken.None);
            var expected = status switch
            {
                HttpStatusCode.NotFound => "not_found", HttpStatusCode.TooManyRequests => "rate_limited",
                HttpStatusCode.Unauthorized => "authentication_required", HttpStatusCode.Forbidden => "blocked",
                _ => "unavailable"
            };
            Check(result.JavdbStatus == expected && result.ReviewStatus == "unavailable" && result.Ratings.Length == 1,
                "JAVDB failure preserves primary metadata and reports " + expected);
        }
        foreach (var number in new[] {null, "", "--magnets", "../../private", "ABC-123;touch x"})
        {
            backend = new Backend(); result = Primary(number);
            await JavdbEnrichment.ApplyAsync(backend, result, 0, 10, false, CancellationToken.None);
            Check(backend.MovieId == null && result.JavdbStatus == "number_unavailable",
                "unverified movie number cannot trigger JAVDB lookup");
        }
        backend = new Backend();
        var users = DispatchProxy.Create<IUserManager, WebChecks.Stub>();
        var library = DispatchProxy.Create<ILibraryManager, WebChecks.Stub>();
        var user = new User("reader", "auth", "reset");
        var item = new Movie(); item.SetProviderId("MetaTube", "Fixture:original");
        ((WebChecks.Stub)(object)users).Call = (method,args) => user;
        ((WebChecks.Stub)(object)library).Call = (method,args) => item;
        var controller = new MetadataController(library, users, backend, NullLogger<MetadataController>.Instance,
            new Settings { JavdbEnabled = true });
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        Check((await controller.GetMetadata(Guid.NewGuid())).Result is UnauthorizedResult && backend.MovieCalls == 0,
            "enabling JAVDB does not bypass Jellyfin authentication");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim("Jellyfin.UserId",Guid.NewGuid().ToString())},"test"));
        result = (ExtendedMetadataResponse)((OkObjectResult)(await controller.GetMetadata(Guid.NewGuid())).Result).Value;
        Check(result.ReviewProvider == "JAVDB" && backend.PrimaryReviewCalls == 0 && backend.MovieCalls == 2,
            "selected JAVDB comments skip the original source's review request");
        item.SetProviderId("MetaTube", "JAVDB:ABC-123");
        backend.ReviewFailure = HttpStatusCode.TooManyRequests;
        result = (ExtendedMetadataResponse)((OkObjectResult)(await controller.GetMetadata(Guid.NewGuid())).Result).Value;
        Check(result.JavdbStatus == "rate_limited" && result.ReviewStatus == "unavailable",
            "direct JAVDB items expose the same restriction status as supplemented items");
    }

    sealed class Settings : IExtendedMetadataSettings
    {
        public bool PanelEnabled => true;
        public bool JavdbEnabled { get; init; }
    }

    sealed class Backend : IMetadataBackend
    {
        public string MovieId, ReviewId;
        public int Offset;
        public bool Lazy;
        public HttpStatusCode? Failure;
        public HttpStatusCode? ReviewFailure;
        public int MovieCalls, PrimaryReviewCalls;
        public Task<MovieInfo> GetMovieAsync(string provider, string id, CancellationToken ct)
        {
            MovieCalls++;
            if (provider == "Fixture") return Task.FromResult(new MovieInfo { Provider=provider, Id=id, Number="ABC-123" });
            Check(provider == "JAVDB", "supplement targets only JAVDB");
            MovieId = id;
            if (Failure.HasValue) throw new HttpRequestException("sensitive upstream error", null, Failure);
            return Task.FromResult(new MovieInfo { Id = id, Number = id, Provider = provider,
                Ratings = new[]{new MovieRating { Source = provider, SourceId = "x1", Value = 4.1, ScaleMax = 5 }} });
        }
        public Task<MovieReviewPage> GetReviewsAsync(string provider, string id, int offset, int limit, bool lazy, CancellationToken ct)
        {
            if (provider == "Fixture") PrimaryReviewCalls++;
            if (ReviewFailure.HasValue) throw new HttpRequestException("sensitive review failure", null, ReviewFailure);
            Check(provider == "JAVDB" && limit == 10, "supplement review request stays bounded and source-specific");
            ReviewId = id; Offset = offset; Lazy = lazy;
            return Task.FromResult(new MovieReviewPage { Items = new[]{new MovieReview { Source = provider, Comment = "Neutral" }} });
        }
    }
}

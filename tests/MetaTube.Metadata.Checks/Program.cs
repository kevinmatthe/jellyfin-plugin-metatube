using System.Text.Json;
using Jellyfin.Plugin.MetaTube.Metadata;
using Jellyfin.Plugin.MetaTube.Helpers;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}

var movie = JsonSerializer.Deserialize<MovieInfo>("""
{"id":"sample","provider":"Example","score":4.2,"ratings":[{"source":"Example","source_id":"sample","value":4.2,"scale_max":5,"vote_count":120,"review_count":null,"distribution":[{"value":5,"count":60}]}],"tags":[{"name":"Sample series","category":"series","source":"Example"},{"name":"sample series","category":"series"},{"name":"Unsafe|separator","category":"label"}]}
""");
Check(movie.Ratings.Length == 1 && movie.Ratings[0].VoteCount == 120, "rating JSON and known vote count");
Check(movie.Ratings[0].ReviewCount is null, "unknown count stays null");
Check(MetadataMapping.GetCommunityRating(movie) == 8.4f, "explicit five point scale normalizes to ten");
movie.Ratings[0].ScaleMax = 10;
Check(MetadataMapping.GetCommunityRating(movie) == 4.2f, "ten point scale is not doubled");
movie.Ratings[0].Value = double.NaN;
Check(MetadataMapping.GetCommunityRating(movie) == 8.4f, "invalid structured rating falls back to legacy");
movie.Score = 0;
Check(MetadataMapping.GetCommunityRating(movie) is null, "missing rating remains absent");
Check(MetadataMapping.GetNativeTags(movie).SequenceEqual(new[]{"Sample series", "Unsafe / separator"}), "native tags are deduplicated and delimiter-safe");
var page = JsonSerializer.Deserialize<MovieReviewPage>("""
{"items":[{"id":"r1","source":"Example","author":"Reader","comment":"A neutral example.","helpful_count":0,"date":"2026-10-19T00:00:00Z"}],"offset":0,"limit":10,"has_more":false,"total":null,"paginated":true}
""");
Check(page.Items.Length == 1 && page.Items[0].HelpfulCount == 0 && page.Total is null, "review page null versus zero semantics");
Check(MetadataMapping.BuildBackendPath("/v1/movies", "Example", "id/with?#:") == "/v1/movies/Example/id%2Fwith%3F%23%3A", "provider identifiers cannot change request path");
Console.WriteLine("All metadata checks passed.");
var goMovie = JsonSerializer.Deserialize<ResponseInfo<MovieInfo>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"metadata_movie.json"))).Data;
var goPage = JsonSerializer.Deserialize<ResponseInfo<MovieReviewPage>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"metadata_review_page.json"))).Data;
Check(goMovie.Ratings.Length == 2 && goMovie.Ratings[0].VoteCount == 12 && goMovie.Ratings[1].VoteCount is null,
    "actual Go movie JSON deserializes without losing known and unknown counts");
Check(goPage.Cached && goPage.FetchedAt.HasValue && goPage.Items[0].Comment.Contains("<script>"),
    "actual Go persisted page JSON preserves timestamps and text");
await WebChecks.Run();
await JavdbChecks.Run();

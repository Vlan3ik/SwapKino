using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

public static class RecommendationEligibility
{
    public const int MinimumVoteCount = 1;

    public static bool IsEligible(Movie movie) =>
        movie.DetailsState == "ready" &&
        !movie.Adult &&
        !string.IsNullOrWhiteSpace(movie.Title) &&
        movie.VoteAverage > 0 &&
        movie.VoteCount >= MinimumVoteCount &&
        (movie.PosterPath is not null || movie.BackdropPath is not null);

    public static IQueryable<Movie> Apply(IQueryable<Movie> query) => query
        .Where(x => x.DetailsState == "ready" && !x.Adult && x.Title != "" &&
            x.VoteAverage > 0 && x.VoteCount >= MinimumVoteCount &&
            (x.PosterPath != null || x.BackdropPath != null));
}

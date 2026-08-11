using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

public static class ImportQueries
{
    public static IQueryable<Movie> LightweightMovies(IQueryable<Movie> query) => query.Select(x => new Movie
    {
        TmdbId = x.TmdbId,
        IsSeries = x.IsSeries,
        Title = x.Title,
        OriginalTitle = x.OriginalTitle,
        ReleaseDate = x.ReleaseDate,
        VoteCount = x.VoteCount
    });
}

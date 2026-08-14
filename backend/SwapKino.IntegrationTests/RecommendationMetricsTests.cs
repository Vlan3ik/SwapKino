using SwapKino.Api;
using Xunit;

namespace SwapKino.IntegrationTests;

public sealed class RecommendationMetricsTests
{
    [Fact]
    public void Ndcg_rewards_relevant_items_near_the_top()
    {
        Assert.True(RecommendationMetrics.NdcgAtK([3, 2, 0, 0], 4) > RecommendationMetrics.NdcgAtK([0, 0, 2, 3], 4));
    }

    [Fact]
    public void Recall_and_coverage_are_bounded_and_deduplicated()
    {
        Assert.Equal(.5, RecommendationMetrics.RecallAtK([1, 1, 4], [1, 2], 3));
        Assert.Equal(.2, RecommendationMetrics.Coverage([1, 1, 2], 10));
    }

    [Fact]
    public void Diversity_is_zero_for_identical_feature_sets()
    {
        IReadOnlySet<int>[] features = [new HashSet<int> { 1, 2 }, new HashSet<int> { 1, 2 }];
        Assert.Equal(0, RecommendationMetrics.IntraListDiversity(features));
    }

    [Fact]
    public void Reel_definitions_keep_strict_theme_guards()
    {
        var sport = ReelDefinitions.All.Single(x => x.Slug == "sport");
        var shortReel = ReelDefinitions.All.Single(x => x.Slug == "short");

        Assert.Equal("sports", sport.Strategy);
        Assert.Contains(6075, sport.KeywordIds!);
        Assert.Empty(sport.Genres);
        Assert.Equal(100, shortReel.MaxRuntime);
        Assert.Null(shortReel.PrimaryGenreId);
        Assert.All(ReelDefinitions.All.Where(x => x.Strategy == "genres" && x.Genres.Length > 0), reel => Assert.Equal(reel.Genres[0], reel.PrimaryGenreId));
    }

    [Fact]
    public void Theme_classifier_requires_the_declared_genre_and_keyword()
    {
        var movie = new Movie { TmdbId = 1, MovieGenres = [new MovieGenre { GenreId = 27 }], MovieKeywords = [new MovieKeyword { KeywordId = 12377 }] };
        var themes = ThemeRegistry.Classify(movie).Select(x => x.Slug).ToHashSet();
        Assert.Contains("horror", themes);
        Assert.DoesNotContain("sport", themes);
    }

    [Fact]
    public void Theme_aliases_keep_existing_reel_urls_compatible()
    {
        Assert.Equal("psychological", ThemeRegistry.CanonicalSlug("na-odnom-dyhanii"));
        Assert.Equal("anime", ThemeRegistry.CanonicalSlug("anime-vecher"));
    }

    [Theory]
    [InlineData("favorite", null, "strong_positive")]
    [InlineData("more_like_this", null, "strong_positive")]
    [InlineData("not_for_me", null, "strong_negative")]
    [InlineData("less_like_this", null, "strong_negative")]
    [InlineData("swipe_left", null, "read")]
    [InlineData("rating", 10.0, "strong_positive")]
    [InlineData("rating", 8.0, "positive")]
    [InlineData("rating", 6.0, "read")]
    [InlineData("rating", 4.0, "negative")]
    public void Feedback_normalizer_preserves_signal_strength(string action, double? value, string expected)
    {
        var normalized = RecommendationFeedback.Normalize(action, value);
        Assert.Equal(expected, normalized?.Type);
    }

    [Fact]
    public void Session_negative_is_not_a_gorse_negative_feedback_type_for_swipes()
    {
        var normalized = RecommendationFeedback.Normalize("swipe_left", null);
        Assert.Equal("read", normalized?.Type);
        Assert.True(normalized?.SessionNegative);
    }

    [Fact]
    public void Recommendation_eligibility_rejects_unenriched_zero_rating_items()
    {
        var movie = new Movie { Title = "Unknown", DetailsState = "ready", VoteAverage = 0, VoteCount = 0, PosterPath = "/poster.jpg" };
        Assert.False(RecommendationEligibility.IsEligible(movie));
        movie.VoteAverage = 7.2;
        movie.VoteCount = 100;
        Assert.True(RecommendationEligibility.IsEligible(movie));
    }

    [Fact]
    public void Feedback_reconciliation_keeps_rating_when_favorite_is_removed()
    {
        var state = new UserMovieState { Rating = 8, Favorite = false, Watched = true };
        var desired = RecommendationFeedback.Current(state, new UserAction { ActionType = "unfavorite" });
        Assert.Contains(desired, x => x.Type == "positive");
        Assert.Contains(desired, x => x.Type == "read");
        Assert.DoesNotContain(desired, x => x.Type == "strong_positive");
    }
}

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
}

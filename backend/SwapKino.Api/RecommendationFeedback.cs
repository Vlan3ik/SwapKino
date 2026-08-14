namespace SwapKino.Api;

public sealed record NormalizedRecommendationFeedback(string Type, double Value, bool SessionNegative);

public static class RecommendationFeedback
{
    public static NormalizedRecommendationFeedback? Normalize(string action, double? value) => action switch
    {
        "impression" or "watched" or "already_watched" => new("read", 1, false),
        "skip" or "swipe_left" => new("read", 1, true),
        "swipe_right" => new("positive", 1, false),
        "favorite" or "more_like_this" => new("strong_positive", 2, false),
        "not_for_me" or "less_like_this" => new("strong_negative", 2, true),
        "not_interested" => new("negative", 1, true),
        "rating" or "rate" or "rate_inline" when value >= 9 => new("strong_positive", value.Value, false),
        "rating" or "rate" or "rate_inline" when value >= 7 => new("positive", value.Value, false),
        "rating" or "rate" or "rate_inline" when value <= 5 => new("negative", 1, true),
        "rating" or "rate" or "rate_inline" => new("read", 1, false),
        _ => null
    };
}

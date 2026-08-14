namespace SwapKino.Api;

public static class RecommendationMetrics
{
    public static double NdcgAtK(IReadOnlyList<int> relevance, int k)
    {
        var values = relevance.Take(k).ToArray();
        var dcg = values.Select((value, index) => (Math.Pow(2, value) - 1) / Math.Log2(index + 2)).Sum();
        var ideal = relevance.OrderByDescending(x => x).Take(k).Select((value, index) => (Math.Pow(2, value) - 1) / Math.Log2(index + 2)).Sum();
        return ideal == 0 ? 0 : dcg / ideal;
    }

    public static double RecallAtK(IEnumerable<int> recommended, IEnumerable<int> relevant, int k)
    {
        var target = relevant.ToHashSet();
        if (target.Count == 0) return 0;
        return recommended.Take(k).ToHashSet().Count(target.Contains) / (double)target.Count;
    }

    public static double IntraListDiversity(IReadOnlyList<IReadOnlySet<int>> features)
    {
        if (features.Count < 2) return 0;
        var distances = new List<double>();
        for (var left = 0; left < features.Count; left++)
            for (var right = left + 1; right < features.Count; right++)
            {
                var union = features[left].Union(features[right]).Count();
                var similarity = union == 0 ? 0 : features[left].Intersect(features[right]).Count() / (double)union;
                distances.Add(1 - similarity);
            }
        return distances.Average();
    }

    public static double Coverage(IEnumerable<int> recommended, int catalogSize)
    {
        return catalogSize <= 0 ? 0 : recommended.ToHashSet().Count / (double)catalogSize;
    }
}

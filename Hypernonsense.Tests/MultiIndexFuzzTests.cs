using System.Numerics.Tensors;
using System.Text;

namespace Hypernonsense.Tests;

[TestClass]
public sealed class MultiIndexFuzzTests
{
    private static float[] RandomUnitVector(Random rng, int dimensions)
    {
        var v = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
            v[i] = rng.NextSingle() * 2 - 1;

        TensorPrimitives.Divide(v, TensorPrimitives.Norm(v.AsSpan()), v);
        return v;
    }

    private static HashSet<int> BruteForceKnn(float[] query, IReadOnlyList<float[]> corpus, int k)
    {
        var scored = new List<(int id, float sim)>(corpus.Count);
        for (var i = 0; i < corpus.Count; i++)
            scored.Add((i, TensorPrimitives.CosineSimilarity(query, corpus[i])));

        scored.Sort((a, b) => b.sim.CompareTo(a.sim));

        var result = new HashSet<int>();
        for (var i = 0; i < Math.Min(k, scored.Count); i++)
            result.Add(scored[i].id);

        return result;
    }

    private record struct Scenario(
        string Label,
        int Dimensions,
        int Planes,
        int Indices,
        int CorpusSize,
        int QueryCount,
        int K,
        int MaxCandidates);

    private record struct FuzzStats(double Recall, double Precision, double AvgCandidates, int Queries, int K);

    [TestMethod]
    public void FuzzPrecisionRecall()
    {
        var scenarios = new[]
        {
            new Scenario("small corpus, low-dim",  Dimensions:  64, Planes: 5, Indices: 4, CorpusSize: 1200, QueryCount: 120, K: 10, MaxCandidates: 128),
            new Scenario("medium corpus, mid-dim", Dimensions: 128, Planes: 6, Indices: 6, CorpusSize: 2000, QueryCount: 140, K: 15, MaxCandidates: 160),
            new Scenario("larger corpus, high-dim",Dimensions: 256, Planes: 7, Indices: 8, CorpusSize: 3000, QueryCount: 120, K: 20, MaxCandidates: 192),
            new Scenario("larger corpus, high-dim",Dimensions: 256, Planes: 7, Indices: 16, CorpusSize: 3000, QueryCount: 120, K: 20, MaxCandidates: 192),
        };

        const int seed = 424242;
        var results = new List<(Scenario scenario, FuzzStats stats)>();

        foreach (var s in scenarios)
        {
            var idx = new MultiIndex<int>(s.Dimensions, s.Planes, s.Indices, seed);
            var rng = new Random(seed + s.Dimensions + s.Planes);
            var corpus = new List<float[]>(s.CorpusSize);

            for (var i = 0; i < s.CorpusSize; i++)
            {
                var v = RandomUnitVector(rng, s.Dimensions);
                corpus.Add(v);
                idx.Add(i, v);
            }

            long truePositives = 0;
            long falsePositives = 0;
            long totalCandidates = 0;

            for (var q = 0; q < s.QueryCount; q++)
            {
                var query = RandomUnitVector(rng, s.Dimensions);
                var truth = BruteForceKnn(query, corpus, s.K);

                var candidates = new List<int>();
                idx.Query(query, candidates, s.MaxCandidates);
                totalCandidates += candidates.Count;

                foreach (var c in candidates)
                {
                    if (truth.Contains(c))
                        truePositives++;
                    else
                        falsePositives++;
                }
            }

            var totalRelevant = (long)s.QueryCount * s.K;
            var recall = totalRelevant > 0 ? (double)truePositives / totalRelevant : 0;
            var retrieved = truePositives + falsePositives;
            var precision = retrieved > 0 ? (double)truePositives / retrieved : 0;
            var avgCandidates = (double)totalCandidates / s.QueryCount;

            results.Add((s, new FuzzStats(recall, precision, avgCandidates, s.QueryCount, s.K)));
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(
            $"{"Scenario",-28} | {"Recall",8} | {"Precision",10} | {"Avg cands",10} | {"Queries",8} | {"K",4}");
        sb.AppendLine(new string('-', 86));

        double recallSum = 0;
        double precisionSum = 0;

        foreach (var (scenario, stats) in results)
        {
            sb.AppendLine(
                $"{scenario.Label,-28} | {stats.Recall,8:P1} | {stats.Precision,10:P1} | {stats.AvgCandidates,10:F1} | {stats.Queries,8} | {stats.K,4}");
            recallSum += stats.Recall;
            precisionSum += stats.Precision;
        }

        var avgRecall = recallSum / results.Count;
        var avgPrecision = precisionSum / results.Count;
        sb.AppendLine(new string('-', 86));
        sb.AppendLine($"{"AVERAGE",-28} | {avgRecall,8:P1} | {avgPrecision,10:P1}");

        Console.WriteLine(sb.ToString());

        foreach (var (scenario, stats) in results)
        {
            Assert.IsGreaterThanOrEqualTo(
                stats.Recall,
                0.45,
                $"Recall too low for '{scenario.Label}': {stats.Recall:P1} (threshold 45%)");
            Assert.IsGreaterThanOrEqualTo(
                stats.Precision,
                0.08,
                $"Precision too low for '{scenario.Label}': {stats.Precision:P1} (threshold 8%)");
        }

        Assert.IsGreaterThanOrEqualTo(avgRecall, 0.55, $"Average recall too low: {avgRecall:P1} (threshold 55%)");
        Assert.IsGreaterThanOrEqualTo(avgPrecision, 0.1, $"Average precision too low: {avgPrecision:P1} (threshold 10%)");
    }
}

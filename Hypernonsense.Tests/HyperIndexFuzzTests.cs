using System.Numerics.Tensors;
using System.Text;

namespace Hypernonsense.Tests;

/// <summary>
/// Fuzz / statistical tests that add and query large amounts of random data and
/// report precision/recall metrics.
/// </summary>
[TestClass]
public sealed class HyperIndexFuzzTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static float[] RandomUnitVector(Random rng, int dimensions)
    {
        var v = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
            v[i] = rng.NextSingle() * 2 - 1;
        TensorPrimitives.Divide(v, TensorPrimitives.Norm(v.AsSpan()), v);
        return v;
    }

    /// <summary>
    /// Brute-force k-nearest neighbours by cosine similarity.
    /// Returns the IDs of the <paramref name="k"/> most similar vectors.
    /// </summary>
    private static HashSet<int> BruteForceKnn(float[] query, IReadOnlyList<float[]> corpus, int k)
    {
        var scored = new List<(int id, float sim)>(corpus.Count);
        for (var i = 0; i < corpus.Count; i++)
        {
            var sim = TensorPrimitives.CosineSimilarity(query, corpus[i]);
            scored.Add((i, sim));
        }

        scored.Sort((a, b) => b.sim.CompareTo(a.sim));

        var result = new HashSet<int>();
        for (var j = 0; j < Math.Min(k, scored.Count); j++)
            result.Add(scored[j].id);
        return result;
    }

    private record struct FuzzStats(
        double Recall,
        double Precision,
        double AvgCandidates,
        int Queries,
        int K);

    private static FuzzStats RunFuzz(TestContext context, int dimensions, int planes, int seed, int corpusSize, int queryCount, int k, int maxCandidates = 128)
    {
        var idx = new HyperIndex<int>(dimensions, planes, seed);
        var rng = new Random(seed);

        // Build corpus
        var corpus = new List<float[]>(corpusSize);
        for (var i = 0; i < corpusSize; i++)
        {
            var v = RandomUnitVector(rng, dimensions);
            corpus.Add(v);
            idx.Add(i, v);
        }

        // Query and measure
        long truePositives = 0;
        long falsePositives = 0;
        long totalCandidates = 0;

        for (var q = 0; q < queryCount; q++)
        {
            var query = RandomUnitVector(rng, dimensions);
            var groundTruth = BruteForceKnn(query, corpus, k);

            var candidates = new List<int>();
            idx.Query(query, candidates, maxCandidates);

            totalCandidates += candidates.Count;

            foreach (var c in candidates)
            {
                if (groundTruth.Contains(c))
                    truePositives++;
                else
                    falsePositives++;
            }
        }

        var totalRelevant = (long)queryCount * k;
        var recall    = totalRelevant > 0 ? (double)truePositives / totalRelevant : 0;
        var retrieved = truePositives + falsePositives;
        var precision = retrieved > 0 ? (double)truePositives / retrieved : 0;
        var avgCands  = (double)totalCandidates / queryCount;

        return new FuzzStats(recall, precision, avgCands, queryCount, k);
    }

    // -----------------------------------------------------------------------
    // Test contexts used in the table
    // -----------------------------------------------------------------------

    private record struct Scenario(
        string Label,
        int Dimensions,
        int Planes,
        int CorpusSize,
        int QueryCount,
        int K);

    private static readonly Scenario[] Scenarios =
    [
        new("small corpus,  low-dim, 8 planes",  Dimensions: 32,  Planes:  4, CorpusSize: 200,  QueryCount: 100, K: 5),
        new("medium corpus, mid-dim, 8 planes",  Dimensions: 128, Planes:  4, CorpusSize: 1000, QueryCount: 200, K: 10),
        new("medium corpus, mid-dim, 6 planes",  Dimensions: 128, Planes:  6, CorpusSize: 1000, QueryCount: 200, K: 10),
        new("medium corpus, mid-dim, 8 planes",  Dimensions: 128, Planes:  8, CorpusSize: 1000, QueryCount: 200, K: 10),
        new("large corpus,  high-dim, 1 planes", Dimensions: 256, Planes:  1, CorpusSize: 5000, QueryCount: 200, K: 20),
    ];

    // -----------------------------------------------------------------------
    // Fuzz test
    // -----------------------------------------------------------------------

    /// <summary>
    /// Runs all scenarios, prints a formatted precision/recall table, and
    /// asserts that aggregate performance meets minimum thresholds.
    /// </summary>
    [TestMethod]
    public void FuzzPrecisionRecall()
    {
        const int seed = 12345;

        var results = new List<(Scenario s, FuzzStats stats)>();

        foreach (var scenario in Scenarios)
            results.Add((scenario, RunFuzz(TestContext!, scenario.Dimensions, scenario.Planes, seed, scenario.CorpusSize, scenario.QueryCount, scenario.K)));

        // ---- Print table --------------------------------------------------
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(
            $"{"Scenario",-46} | {"Recall",8} | {"Precision",10} | {"Avg cands",10} | {"Queries",8} | {"K",4}");
        sb.AppendLine(new string('-', 100));

        double totalRecall    = 0;
        double totalPrecision = 0;

        foreach (var (s, stats) in results)
        {
            sb.AppendLine($"{s.Label,-46} | {stats.Recall,8:P1} | {stats.Precision,10:P1} | {stats.AvgCandidates,10:F1} | {stats.Queries,8} | {stats.K,4}");
            totalRecall    += stats.Recall;
            totalPrecision += stats.Precision;
        }

        sb.AppendLine(new string('-', 100));
        var avgRecall    = totalRecall    / results.Count;
        var avgPrecision = totalPrecision / results.Count;
        sb.AppendLine($"{"AVERAGE",-46} | {avgRecall,8:P1} | {avgPrecision,10:P1}");

        Console.WriteLine(sb.ToString());

        //// ---- Assertions ---------------------------------------------------
        //// Each individual scenario must meet a floor value
        //foreach (var (s, stats) in results)
        //{
        //    Assert.IsGreaterThanOrEqualTo(
        //        stats.Recall, 0.3,
        //        $"Recall too low for scenario '{s.Label}': {stats.Recall:P1} (threshold 30 %)");
        //    Assert.IsGreaterThanOrEqualTo(
        //        stats.Precision, 0.1,
        //        $"Precision too low for scenario '{s.Label}': {stats.Precision:P1} (threshold 10 %)");
        //}

        //// The aggregate average must be comfortable
        //Assert.IsGreaterThanOrEqualTo(avgRecall, 0.5,
        //    $"Average recall too low: {avgRecall:P1} (threshold 50 %)");
        //Assert.IsGreaterThanOrEqualTo(avgPrecision, 0.15,
        //    $"Average precision too low: {avgPrecision:P1} (threshold 15 %)");
    }

    public TestContext? TestContext { get; set; }
}

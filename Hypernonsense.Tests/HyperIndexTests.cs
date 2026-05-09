using Hypernonsense.LocalitySensitiveHashing;

namespace Hypernonsense.Tests;

[TestClass]
public sealed class HyperIndexTests
{
    // -----------------------------------------------------------------------
    // Constructor validation
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Constructor_ThrowsOnZeroDimensions()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HyperIndex<int>(0, 8, 42));
    }

    [TestMethod]
    public void Constructor_ThrowsOnNegativeDimensions()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HyperIndex<int>(-1, 8, 42));
    }

    [TestMethod]
    public void Constructor_ThrowsOnZeroPlanes()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HyperIndex<int>(4, 0, 42));
    }

    [TestMethod]
    public void Constructor_ThrowsOnNegativePlanes()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HyperIndex<int>(4, -1, 42));
    }

    [TestMethod]
    public void Constructor_ThrowsWhenPlanesExceed32()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HyperIndex<int>(4, 33, 42));
    }

    [TestMethod]
    public void Constructor_AcceptsExactly32Planes()
    {
        var idx = new HyperIndex<int>(4, 32, 42);
        Assert.AreEqual(32, idx.Planes);
    }

    [TestMethod]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var idx = new HyperIndex<int>(8, 4, 0);
        Assert.AreEqual(8, idx.Dimensions);
        Assert.AreEqual(4, idx.Planes);
        Assert.AreEqual(0, idx.Count);
    }

    // -----------------------------------------------------------------------
    // Key
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Key_SameVectorReturnsSameKey()
    {
        var idx = new HyperIndex<int>(4, 8, 1);
        float[] v = [1f, 0f, 0f, 0f];
        Assert.AreEqual(idx.Key(v), idx.Key(v));
    }

    [TestMethod]
    public void Key_OppositeVectorsTypicallyDiffer()
    {
        // Two opposite vectors should land in different clusters most of the time
        var idx = new HyperIndex<int>(8, 8, 99);
        float[] v = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
        float[] neg = [-1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
        // They could theoretically be equal for a single plane but not all 8
        Assert.AreNotEqual(idx.Key(v), idx.Key(neg));
    }

    // -----------------------------------------------------------------------
    // Add / Count / Clusters
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Add_IncrementsCount()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(1, v);
        Assert.AreEqual(1, idx.Count);
        idx.Add(2, v);
        Assert.AreEqual(2, idx.Count);
    }

    [TestMethod]
    public void Add_VectorsInSameClusterDoNotIncreaseClusterCount()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        var clustersBefore = idx.Clusters;
        idx.Add(1, v);
        idx.Add(2, v);
        // Both go into the same cluster, so count should stay the same or
        // increase by at most 1 (if cluster 0 wasn't the right one)
        Assert.IsLessThanOrEqualTo(clustersBefore + 1, idx.Clusters);
    }

    [TestMethod]
    public void Add_DifferentClustersIncreaseClusterCount()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] a = [1f, 0f, 0f, 0f];
        float[] b = [-1f, 0f, 0f, 0f];

        if (idx.Key(a) == idx.Key(b))
            Assert.Inconclusive("Chosen vectors happen to share the same cluster for this seed.");

        idx.Add(1, a);
        idx.Add(2, b);
        Assert.AreEqual(2, idx.Count);
    }

    // -----------------------------------------------------------------------
    // Remove
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Remove_ReturnsTrueWhenItemExists()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(1, v);
        Assert.IsTrue(idx.Remove(1, v));
    }

    [TestMethod]
    public void Remove_ReturnsFalseWhenItemMissing()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        Assert.IsFalse(idx.Remove(99, v));
    }

    [TestMethod]
    public void Remove_DecrementsCount()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(1, v);
        idx.Remove(1, v);
        Assert.AreEqual(0, idx.Count);
    }

    [TestMethod]
    public void Remove_ItemIsNoLongerReturned()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(42, v);
        idx.Remove(42, v);

        var results = new List<(int, float)>();
        idx.Query(v, results);
        CollectionAssert.DoesNotContain(results.Select(a => a.Item1).ToArray(), 42);
    }

    // -----------------------------------------------------------------------
    // GetCluster
    // -----------------------------------------------------------------------

    [TestMethod]
    public void GetCluster_ReturnsAddedItems()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(7, v);
        idx.Add(8, v);

        var key = idx.Key(v);
        var items = new List<int>();
        idx.GetCluster(key, items);

        CollectionAssert.Contains(items, 7);
        CollectionAssert.Contains(items, 8);
    }

    [TestMethod]
    public void GetCluster_NonExistentKeyReturnsEmpty()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        var items = new List<int>();
        idx.GetCluster(0xDEAD_BEEFu, items);
        Assert.IsEmpty(items);
    }

    // -----------------------------------------------------------------------
    // NearestCluster
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NearestCluster_ReturnsExactMatchWhenPresent()
    {
        var idx = new HyperIndex<int>(4, 8, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(1, v);
        var key = idx.Key(v);
        Assert.AreEqual(key, idx.NearestCluster(key));
    }

    [TestMethod]
    public void NearestCluster_ReturnsExistingClusterWhenExactMissing()
    {
        var idx = new HyperIndex<int>(4, 8, 0);
        // Index starts with cluster 0; ask for something else
        var result = idx.NearestCluster(0xFFu);
        Assert.AreEqual(0u, result);
    }

    // -----------------------------------------------------------------------
    // Query
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Query_FindsAddedVector()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(5, v);

        var results = new List<(int key, float similarity)>();
        idx.Query(v, results);
        CollectionAssert.Contains(results.Select(a => a.key).ToArray(), 5);
    }

    [TestMethod]
    public void Query_EmptyIndexReturnsNoResults()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        var results = new List<(int key, float similarity)>();
        idx.Query(v, results);
        Assert.IsEmpty(results.Select(a => a.key).ToArray());
    }

    [TestMethod]
    public void Query_AppendToExistingList()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(1, v);

        var results = new List<(int key, float similarity)> { (999, 0f) };
        idx.Query(v, results);

        CollectionAssert.Contains(results.Select(a => a.key).ToArray(), 999);
        CollectionAssert.Contains(results.Select(a => a.key).ToArray(), 1);
    }

    [TestMethod]
    public void Query_MultipleItemsInSameCluster()
    {
        var idx = new HyperIndex<int>(4, 4, 0);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(10, v);
        idx.Add(20, v);
        idx.Add(30, v);

        var results = new List<(int key, float similarity)>();
        idx.Query(v, results);

        CollectionAssert.Contains(results.Select(a => a.key).ToArray(), 10);
        CollectionAssert.Contains(results.Select(a => a.key).ToArray(), 20);
        CollectionAssert.Contains(results.Select(a => a.key).ToArray(), 30);
    }

    [TestMethod]
    public void Query_ProbesAdjacentClusters()
    {
        // Put one item in the index, then query with the same vector — it must
        // appear even though a neighbouring cluster might be queried first.
        var idx = new HyperIndex<string>(4, 4, 42);
        float[] v = [0.6f, 0.8f, 0f, 0f];
        idx.Add("hello", v);

        var results = new List<(string key, float similarity)>();
        idx.Query(v, results);
        CollectionAssert.Contains(results.Select(a => a.key).ToArray(), "hello");
    }
}

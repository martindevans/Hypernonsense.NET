namespace Hypernonsense.Tests;

[TestClass]
public sealed class MultiIndexTests
{
    [TestMethod]
    public void Constructor_WithNoIndices_QueryReturnsNoResults()
    {
        var idx = new MultiIndex<int>(dimensions: 4, planes: 4, indices: 0, seed: 123);
        float[] query = [1f, 0f, 0f, 0f];

        var results = new List<int>();
        idx.Query(query, results);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void Add_QueryFindsInsertedItem()
    {
        var idx = new MultiIndex<int>(dimensions: 4, planes: 4, indices: 4, seed: 123);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(42, v);

        var results = new List<int>();
        idx.Query(v, results);

        CollectionAssert.Contains(results, 42);
    }

    [TestMethod]
    public void Query_AppendsToExistingOutput()
    {
        var idx = new MultiIndex<int>(dimensions: 4, planes: 4, indices: 4, seed: 123);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(100, v);

        var results = new List<int> { 999 };
        idx.Query(v, results);

        CollectionAssert.Contains(results, 999);
        CollectionAssert.Contains(results, 100);
    }

    [TestMethod]
    public void Query_DeduplicatesAcrossIndices()
    {
        var idx = new MultiIndex<int>(dimensions: 4, planes: 4, indices: 6, seed: 123);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(7, v);

        var results = new List<int>();
        idx.Query(v, results);

        Assert.AreEqual(1, results.Count(r => r == 7));
    }

    [TestMethod]
    public void Remove_ReturnsTrueWhenItemExists()
    {
        var idx = new MultiIndex<int>(dimensions: 4, planes: 4, indices: 4, seed: 123);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(1, v);

        Assert.IsTrue(idx.Remove(1, v));
    }

    [TestMethod]
    public void Remove_ReturnsFalseWhenItemMissing()
    {
        var idx = new MultiIndex<int>(dimensions: 4, planes: 4, indices: 4, seed: 123);
        float[] v = [1f, 0f, 0f, 0f];

        Assert.IsFalse(idx.Remove(999, v));
    }

    [TestMethod]
    public void Remove_ItemIsNoLongerReturnedByQuery()
    {
        var idx = new MultiIndex<int>(dimensions: 4, planes: 4, indices: 4, seed: 123);
        float[] v = [1f, 0f, 0f, 0f];
        idx.Add(55, v);
        idx.Remove(55, v);

        var results = new List<int>();
        idx.Query(v, results);

        CollectionAssert.DoesNotContain(results, 55);
    }
}

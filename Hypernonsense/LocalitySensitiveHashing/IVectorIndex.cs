namespace Hypernonsense.LocalitySensitiveHashing;

public interface IVectorIndex<TKey>
{
    /// <summary>
    /// How many dimensions are the vectors in this index
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Add a new vector to the index
    /// </summary>
    /// <param name="id"></param>
    /// <param name="vector"></param>
    void Add(TKey id, ReadOnlySpan<float> vector);

    /// <summary>
    /// Remove a vector from the index
    /// </summary>
    /// <param name="id"></param>
    /// <param name="vector"></param>
    /// <returns></returns>
    bool Remove(TKey id, ReadOnlySpan<float> vector);

    /// <summary>
    /// Query the index, finding potential nearest vectors up to a soft max
    /// </summary>
    /// <param name="vector"></param>
    /// <param name="output"></param>
    /// <param name="max"></param>
    void Query(ReadOnlySpan<float> vector, List<(TKey key, float similarity)> output, int max = 128);
}
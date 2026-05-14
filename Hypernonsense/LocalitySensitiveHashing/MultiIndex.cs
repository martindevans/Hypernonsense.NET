using System.Runtime.InteropServices;

namespace Hypernonsense.LocalitySensitiveHashing;

/// <summary>
/// An index made from multiple hyper indices, cross references to improve accuracy
/// </summary>
public class MultiIndex<TKey>
    : IVectorIndex<TKey> where TKey : notnull
{
    public int Dimensions { get; }
    
    private readonly List<HyperIndex<TKey>> _indices = [ ];
    
    public MultiIndex(int dimensions, int planes, int indices, int seed)
    {
        Dimensions = dimensions;
        for (var i = 0; i < indices; i++)
            _indices.Add(new HyperIndex<TKey>(dimensions, planes, seed + i));
    }

    public void Add(TKey id, ReadOnlySpan<float> vector)
    {
        foreach (var index in _indices)
            index.Add(id, vector);
    }

    public bool Remove(TKey id, ReadOnlySpan<float> vector)
    {
        var result = false;
        foreach (var index in _indices)
            result |= index.Remove(id, vector);
        return result;
    }

    public void Query(ReadOnlySpan<float> vector, List<(TKey key, float similarity)> output, int max = 128)
    {
        var dict = new Dictionary<TKey, float>();
        var list = new List<(TKey key, float similarity)>();
        
        foreach (var index in _indices)
        {
            // Query the vector
            list.Clear();
            index.Query(vector, list, max * 2);

            // Increment counters (bigger increment if it's the exact right cluster)
            foreach (var (key, similarity) in list)
            {
                ref var counter = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out _);
                counter += similarity;
            }
        }
        
        // Take the best results
        output.AddRange(dict.OrderByDescending(a => a.Value).Take(max).Select(a => (a.Key, a.Value)));
    }
}
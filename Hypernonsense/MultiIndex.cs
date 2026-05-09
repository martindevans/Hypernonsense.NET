using System.Runtime.InteropServices;

namespace Hypernonsense;

/// <summary>
/// An index made from multiple hyper indices, cross references to improve accuracy
/// </summary>
public class MultiIndex<TKey>
    : IVectorIndex<TKey> where TKey : notnull
{
    private readonly List<HyperIndex<TKey>> _indices = [ ];
    
    public MultiIndex(int dimensions, int planes, int indices, int seed)
    {
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

    public void Query(ReadOnlySpan<float> vector, List<TKey> output, int max = 128)
    {
        var dict = new Dictionary<TKey, ushort>();
        var list = new List<TKey>();
        
        foreach (var index in _indices)
        {
            // Get the actual cluster this vector would be in
            var k = index.Key(vector);
            
            // Get the nearest vector
            var nk = index.NearestCluster(k);

            // Get all items in nearest cluster
            list.Clear();
            index.GetCluster(nk, list);

            // Increment counters (bigger increment if it's the exact right cluster)
            var inc = k == nk ? (ushort)2 : (ushort)1;
            foreach (var key in list)
            {
                ref var counter = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out _);
                counter = ushort.CreateSaturating(counter + inc);
            }
            
            // Trim if the dictionary gets really huge
            if (dict.Count > max * 4)
            {
                var threshold = dict.Select(a => a.Value).Order().SkipLast(max).Last();
                
                foreach (var (key, count) in dict)
                    if (count < threshold)
                        dict.Remove(key);
            }
        }
        
        output.AddRange(dict.Keys);
    }
}
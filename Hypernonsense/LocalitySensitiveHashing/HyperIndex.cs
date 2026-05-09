using System.Numerics;
using System.Numerics.Tensors;

namespace Hypernonsense.LocalitySensitiveHashing;

/// <summary>
/// A simple hyperindex
/// </summary>
/// <typeparam name="TKey"></typeparam>
public class HyperIndex<TKey>
    : IVectorIndex<TKey>
{
    public int Dimensions { get; }
    public int Planes { get; }

    private readonly ReadOnlyMemory<float> _planes;
    private readonly Dictionary<uint, List<TKey>> _clusters = [ ];

    /// <summary>
    /// How many vectors have been inserted
    /// </summary>
    public int Count { get; private set; }
    
    /// <summary>
    /// How many clusters exist in total
    /// </summary>
    public int Clusters => _clusters.Count;
    
    public HyperIndex(int dimensions, int planes, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimensions, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(planes, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(planes, 32);
            
        Dimensions = dimensions;
        Planes = planes;

        // Pick random planes normals
        var planesData = new float[planes * dimensions];
        for (var i = 0; i < planes; i++)
            VectorHelper.RandomUnitVector(seed + i, planesData.AsSpan(i * dimensions, dimensions));
        _planes = planesData;

        // Ensure there is always at least one cluster
        _clusters.Add(0, []);
    }

    /// <summary>
    /// Given a vector, calculate the cluster key
    /// </summary>
    /// <param name="vector"></param>
    /// <returns></returns>
    public uint Key(ReadOnlySpan<float> vector)
    {
        uint result = 0;

        for (var i = 0; i < Planes; i++)
        {
            var planeVec = _planes.Span.Slice(i * Dimensions, Dimensions);
            var d = TensorPrimitives.Dot(planeVec, vector);

            result = (result << 1) | (uint)(d > 0 ? 1 : 0);
        }

        return result;
    }

    /// <summary>
    /// Add a new vector to the index
    /// </summary>
    /// <param name="id"></param>
    /// <param name="vector"></param>
    public void Add(TKey id, ReadOnlySpan<float> vector)
    {
        var clusterKey = Key(vector);

        if (!_clusters.TryGetValue(clusterKey, out var cluster))
        {
            cluster = [ ];
            _clusters.Add(clusterKey, cluster);
        }

        Count++;
        cluster.Add(id);
    }

    /// <summary>
    /// Remove a vector from the index
    /// </summary>
    /// <param name="id"></param>
    /// <param name="vector"></param>
    /// <returns></returns>
    public bool Remove(TKey id, ReadOnlySpan<float> vector)
    {
        var clusterKey = Key(vector);

        if (!_clusters.TryGetValue(clusterKey, out var cluster))
            return false;

        if (cluster.Remove(id))
        {
            Count--;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Find the nearest cluster to the cluster vector
    /// </summary>
    /// <param name="queryKey"></param>
    /// <returns></returns>
    public uint NearestCluster(uint queryKey)
    {
        // Check if that specific cluster exists
        if (_clusters.ContainsKey(queryKey))
            return queryKey;
            
        // Iterate over all clusters to find the closest
        var bestKey = 0u;
        var bestHamming = Hamming(0, queryKey);
        foreach (var (key, _) in _clusters)
        {
            var h = Hamming(key, queryKey);

            if (h < bestHamming)
            {
                bestKey = key;
                bestHamming = h;
            }
        }

        return bestKey;
    }

    /// <summary>
    /// Get all the vectors in the given cluster
    /// </summary>
    /// <param name="key"></param>
    /// <param name="output"></param>
    public void GetCluster(uint key, List<TKey> output)
    {
        if (_clusters.TryGetValue(key, out var cluster))
            output.AddRange(cluster);
    }

    public void Query(ReadOnlySpan<float> vector, List<(TKey key, float similarity)> output, int max = 128)
    {
        var planesSpan = _planes.Span;

        // compute dot products + bit signs
        Span<(float confidence, int bit)> dots = stackalloc (float, int)[Planes];
        for (var i = 0; i < Planes; i++)
        {
            var plane = planesSpan.Slice(i * Dimensions, Dimensions);
            var d = TensorPrimitives.Dot(plane, vector);

            dots[i] = (float.Abs(d), Convert.ToInt32(d > 0));
        }

        // compute key
        uint baseKey = 0;
        for (var i = 0; i < Planes; i++)
            baseKey = (baseKey << 1) | (uint)dots[i].bit;

        // order bits by confidence (low |d| first)
        var order = new int[Planes];
        for (var i = 0; i < Planes; i++)
            order[i] = i;
        
        for (var i = 0; i < Planes - 1; i++)
            for (var j = i + 1; j < Planes; j++)
                if (dots[order[j]].confidence < dots[order[i]].confidence)
                    (order[i], order[j]) = (order[j], order[i]);

        var seen = new HashSet<uint>();

        var remaining = max;

        // radius 0
        TryAdd(baseKey, 1f);
        if (remaining <= 0)
            return;

        // radius 1
        for (var i = 0; i < Planes && remaining > 0; i++)
        {
            var k1 = baseKey ^ (1u << order[i]);
            TryAdd(k1, 0.5f);
        }

        if (remaining <= 0)
            return;

        // radius 2
        for (var i = 0; i < Planes / 2 && remaining > 0; i++)
        for (var j = i + 1; j < Planes / 2 && remaining > 0; j++)
        {
            var k2 = baseKey ^ (1u << order[i]) ^ (1u << order[j]);
            TryAdd(k2, 0.25f);
        }

        if (remaining <= 0)
            return;

        // radius 3 (partial)
        for (var i = 0; i < Planes / 2 && remaining > 0; i++)
        for (var j = i + 1; j < Planes / 2 && remaining > 0; j++)
        for (var k = j + 1; k < Planes / 2 && remaining > 0; k++)
        {
            var k3 = baseKey ^ (1u << order[i]) ^ (1u << order[j]) ^ (1u << order[k]);

            TryAdd(k3, 0.125f);
        }

        return;

        int AddByKey(uint key, float sim)
        {
            if (!seen.Add(key))
                return 0;

            if (_clusters.TryGetValue(key, out var cluster))
            {
                output.EnsureCapacity(output.Count + cluster.Count);
                foreach (var item in cluster)
                    output.Add((item, sim));
                
                return cluster.Count;
            }
            return 0;
        }

        void TryAdd(uint key, float sim)
        {
            if (remaining <= 0)
                return;
            remaining -= AddByKey(key, sim);
        }
    }

    private static uint Hamming(uint a, uint b)
    {
        var xor = a ^ b;
        return (uint)BitOperations.PopCount(xor);
    }
}
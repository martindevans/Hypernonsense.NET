using System.Numerics;
using System.Numerics.Tensors;

namespace Hypernonsense;

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
    private readonly Dictionary<uint, List<TKey>> _clusters = new();

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

        Count--;
        return cluster.Remove(id);
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
                bestKey = key;
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

    /// <summary>
    /// Query the index, finding potential nearest vectors up to a soft max
    /// </summary>
    /// <param name="vector"></param>
    /// <param name="output"></param>
    /// <param name="max"></param>
    public void Query(ReadOnlySpan<float> vector, List<TKey> output, int max = 128)
    {
        // What cluster should this be in
        var key = Key(vector);
        
        // Find the nearest cluster
        var near = NearestCluster(key);

        // Return all results in that cluster
        if (_clusters.TryGetValue(near, out var cluster))
        {
            output.AddRange(cluster);
            max -= cluster.Count;
        }
        
        // Probe all adjacent clusters
        if (max > 0)
        {
            for (var i = 0; i < Planes; i++)
            {
                // Flip a single bit in the key
                var k = key ^ (1u << i);

                // Ensure we don't add the "near" cluster twice
                if (k == near)
                    continue;

                // Add all items in this cluster
                if (_clusters.TryGetValue(k, out var ncluster))
                {
                    output.AddRange(ncluster);
                    max -= ncluster.Count;
                }
            }
        }
    }

    private static uint Hamming(uint a, uint b)
    {
        var xor = a ^ b;
        return (uint)BitOperations.PopCount(xor);
    }
}
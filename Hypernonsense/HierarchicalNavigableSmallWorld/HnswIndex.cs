using Dapper;
using Dapper.Contrib.Extensions;
using Hypernonsense.LocalitySensitiveHashing;
using System.Collections.Concurrent;
using System.Data;
using System.Runtime.InteropServices;

namespace Hypernonsense.HierarchicalNavigableSmallWorld;

public class HnswIndex
    : IVectorIndex<long>
{
    private const int LAYERS = 32;
    private const int MIN_CONNECTIVITY = 4;
    private const int MAX_CONNECTIVITY = 16;
    
    public int Dimensions { get; }
    
    private readonly string _name;
    private readonly IHnswDbConnectionFactory _db;

    private readonly ConcurrentBag<Half[]> _quantisedHalfPool = [ ];
    private readonly ConcurrentBag<byte[]> _quantisedBytePool = [ ];
    
    public HnswIndex(string name, int dimensions, IHnswDbConnectionFactory db)
    {
        Dimensions = dimensions;
        _name = name;
        _db = db;
    }

    public void Add(long id, ReadOnlySpan<float> vector)
    {
        using var conn = _db.Create();
        
        // Ensure the database is initialised
        Init(conn);
        
        // Get the numeric ID of this index
        var indexId = GetIndexId(conn);
        
        // Store the raw vector
        StoreVector(conn, id, indexId, vector);
        
        // Decide which layer this vector will be inserted up to
        var maxLayer = ChooseLayer(id);

        // Query as normal, from top down. Keeping track of the descending links we follow
        Span<long> descenders = stackalloc long[LAYERS];
        Query(indexId, vector, descenders);
        
        // Insert vector into all the layers we decided on, storing links
        // to the descending nodes.
        // todo: ^
        
        
    }

    private void StoreVector(IDbConnection conn, long vectorId, long indexId, ReadOnlySpan<float> vector)
    {
        // Get an array to quantise into
        if (!_quantisedHalfPool.TryTake(out var halfs))
            halfs = new Half[vector.Length];
        if (!_quantisedBytePool.TryTake(out var bytes))
            bytes = new byte[vector.Length];

        // Quantise to half
        for (var i = 0; i < vector.Length; i++)
            halfs[i] = (Half)vector[i];

        // Convert to byte
        MemoryMarshal.Cast<Half, byte>(halfs).CopyTo(bytes.AsSpan());

        // Store into DB
        conn.Insert(new HnswVector(indexId, vectorId, bytes));
        
        // Return pooled memory
        _quantisedHalfPool.Add(halfs);
        _quantisedBytePool.Add(bytes);
    }
    
    /// <summary>
    /// Remove the vector with the given ID
    /// </summary>
    /// <param name="id"></param>
    /// <param name="vector"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public bool Remove(long id, ReadOnlySpan<float> vector)
    {
        using var conn = _db.Create();

        // Ensure the database is initialised
        Init(conn);

        // Get the numeric ID of this index
        var indexId = GetIndexId(conn);
        
        // Get the links involving this vector
        var deletedLinks = conn.Query<HnswLayerLink>("""
                                                     SELECT * FROM HnswLinks
                                                     WHERE IndexId = @IdxId
                                                     AND (SrcId = @VecId OR DstId = @VecId)
                                                     """, new { IdxId = indexId, VecId = id }).ToList();
        
        // Delete the links involving this vector
        var deleted = conn.Execute("""
                                   DELETE FROM HnswLinks
                                   WHERE IndexId = @IdxId
                                   AND (SrcId = @VecId OR DstId = @VecId)
                                   """, new { IdxId = indexId, VecId = id });
        if (deleted == 0)
            return false;
        
        // Stitch together links, to ensure graph does not become sparse
        for (var i = 0; i < LAYERS; i++)
            StichLayerLinks(conn, indexId, i, deletedLinks);

        return true;
    }

    /// <summary>
    /// Query the index, finding a set of similar vectors
    /// </summary>
    /// <param name="vector"></param>
    /// <param name="output"></param>
    /// <param name="max"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Query(ReadOnlySpan<float> vector, List<(long key, float similarity)> output, int max = 128)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Search the given index, finding the IDs of the "descender" nodes
    /// </summary>
    /// <param name="indexId"></param>
    /// <param name="vector"></param>
    /// <param name="descenders"></param>
    private void Query(long indexId, ReadOnlySpan<float> vector, Span<long> descenders)
    {
        // todo:
        // 1. Start at the top layer, pick a random node
        // 2. Check all links from current node, measuring similarity to query
        // 3. Follow the link that leads to the most similar vertex
        // 4. If none of the links are better than the current node, move "down" a layer (store descender)
        // 5. goto 2
        
        throw new NotImplementedException();
    }

    private long GetIndexId(IDbConnection db)
    {
        return db.ExecuteScalar<long>(
            """
            INSERT OR IGNORE INTO HnswLayerNameMappings (Name) VALUES (@name);
            SELECT ID FROM HnswLayerNameMappings WHERE Name = @name;
            """,
            new
            {
                name = _name
            }
        );
    }

    private static int ChooseLayer(long id)
    {
        var layer = 0;

        var x = (ulong)id;
        for (var i = 0; i < LAYERS; i++)
        {
            // xorshift64*
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;

            // use lowest bit as a fair coin
            if ((x & 1UL) == 0)
                break;

            layer++;
        }

        return layer;
    }

    private void StichLayerLinks<TList>(IDbConnection conn, long indexId, int layer, TList links)
        where TList : IReadOnlyList<HnswLayerLink>
    {
        // Given a set of delete links, fixup the given layer to prevent sparsity
        throw new NotImplementedException();
    }
    
    /// <summary>
    /// Initialise the DB (create tables/indices etc). It is safe to call this multiple times, no-op if database is already initialised.
    /// </summary>
    /// <param name="db"></param>
    private static void Init(IDbConnection db)
    {
        // Assign a number to index names, so other tables can just store the number
        db.Execute("""
                   CREATE TABLE IF NOT EXISTS `HnswLayerNameMappings` (
                        `Name` TEXT NOT NULL,
                        `ID` INTEGER PRIMARY KEY AUTOINCREMENT
                   );
                   """);

        db.Execute("""
                   CREATE TABLE IF NOT EXISTS `HnswVectors` (
                        `IndexId` INTEGER NOT NULL,
                        `VectorId` INTEGER NOT NULL,
                        `Data` BLOB
                   );
                   """);
        
        db.Execute("""
                   CREATE TABLE IF NOT EXISTS `HnswLinks` (
                        `IndexId` INTEGER NOT NULL,
                        `LayerId` INTEGER NOT NULL,
                        `SrcId` INTEGER NOT NULL,
                        `DstId` INTEGER NOT NULL
                   );
                   """);
        
        db.Execute("""
                   CREATE INDEX IF NOT EXISTS "HnswLinks_Index_Src" ON "HnswLinks" (
                   	"IndexId"	ASC,
                   	"LayerId"	ASC,
                   	"SrcId"	ASC
                   );
                   """);

        db.Execute("""
                   CREATE INDEX IF NOT EXISTS "HnswLinks_Index_Dst" ON "HnswLinks" (
                   	"IndexId"	ASC,
                   	"LayerId"	ASC,
                   	"DstId"	ASC
                   );
                   """);

        //foreach (var r in db.Query<dynamic>("EXPLAIN QUERY PLAN SELECT * FROM HnswLinks WHERE IndexId = 99 AND DstId = 2 AND LayerId = 3"))
        //    Console.WriteLine(r.detail);
    }

    internal record HnswLayerNameMapping(string Name, long ID);
    internal record HnswVector(long IndexId, long VectorId, byte[] Data);
    internal record HnswLayerLink(string IndexId, long LayerId, long SrcId, long DstId);
}

public interface IHnswDbConnectionFactory
{
    IDbConnection Create();
}
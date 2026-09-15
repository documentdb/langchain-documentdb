using Microsoft.Extensions.VectorData;
using MongoDB.Bson;

namespace DocumentDB.LangChain;

internal static class DocumentDBCommandBuilder
{
    public static BsonDocument CreateVectorIndex(
        string collectionName,
        VectorMapping vector,
        DocumentDBVectorStoreOptions options)
    {
        var indexKind = vector.Definition.IndexKind ?? IndexKind.DiskAnn;
        var indexOptions = new BsonDocument
        {
            { "kind", ToDocumentDBIndexKind(indexKind) },
            { "dimensions", vector.Definition.Dimensions },
            { "similarity", ToDocumentDBSimilarity(vector.Definition.DistanceFunction) },
        };

        switch (indexKind)
        {
            case IndexKind.IvfFlat:
                indexOptions["numLists"] = options.IvfNumLists;
                break;
            case IndexKind.Hnsw:
                indexOptions["m"] = options.HnswM;
                indexOptions["efConstruction"] = options.HnswEfConstruction;
                break;
            case IndexKind.DiskAnn:
                indexOptions["maxDegree"] = options.DiskAnnMaxDegree;
                indexOptions["lBuild"] = options.DiskAnnBuildCandidates;
                break;
            default:
                throw new NotSupportedException($"DocumentDB does not support index kind '{indexKind}'.");
        }

        return new BsonDocument
        {
            { "createIndexes", collectionName },
            {
                "indexes",
                new BsonArray
                {
                    new BsonDocument
                    {
                        { "name", GetIndexName(vector.StorageName) },
                        { "key", new BsonDocument(vector.StorageName, "cosmosSearch") },
                        { "cosmosSearchOptions", indexOptions },
                    },
                }
            },
        };
    }

    public static BsonDocument[] CreateSearchPipeline(
        VectorMapping vector,
        ReadOnlyMemory<float> query,
        int top,
        int skip,
        double? scoreThreshold,
        BsonDocument? filter,
        DocumentDBVectorStoreOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        if (query.IsEmpty)
        {
            throw new ArgumentException("The query vector must not be empty.", nameof(query));
        }

        var search = new BsonDocument
        {
            { "vector", new BsonArray(query.Span.ToArray()) },
            { "path", vector.StorageName },
            { "k", checked(top + skip) },
        };
        if (filter is not null)
        {
            search["filter"] = filter;
        }

        switch (vector.Definition.IndexKind ?? IndexKind.DiskAnn)
        {
            case IndexKind.IvfFlat:
                search["nProbes"] = options.IvfNumProbes;
                break;
            case IndexKind.Hnsw:
                search["efSearch"] = options.HnswEfSearch;
                break;
            case IndexKind.DiskAnn:
                search["lSearch"] = options.DiskAnnSearchCandidates;
                break;
        }

        var pipeline = new List<BsonDocument>
        {
            new("$search", new BsonDocument
            {
                { "cosmosSearch", search },
                { "returnStoredSource", true },
            }),
            new("$project", new BsonDocument
            {
                { "similarityScore", new BsonDocument("$meta", "searchScore") },
                { "document", "$$ROOT" },
            }),
        };

        if (scoreThreshold is { } threshold)
        {
            pipeline.Add(new BsonDocument("$match", new BsonDocument(
                "similarityScore",
                new BsonDocument("$gte", threshold))));
        }

        if (skip > 0)
        {
            pipeline.Add(new BsonDocument("$skip", skip));
        }

        pipeline.Add(new BsonDocument("$limit", top));
        return pipeline.ToArray();
    }

    public static string GetIndexName(string storageName) =>
        $"{storageName.Replace(".", "_", StringComparison.Ordinal)}_vector";

    private static string ToDocumentDBIndexKind(string indexKind) => indexKind switch
    {
        IndexKind.IvfFlat => "vector-ivf",
        IndexKind.Hnsw => "vector-hnsw",
        IndexKind.DiskAnn => "vector-diskann",
        _ => throw new NotSupportedException($"DocumentDB does not support index kind '{indexKind}'."),
    };

    private static string ToDocumentDBSimilarity(string? distanceFunction) => distanceFunction switch
    {
        null or DistanceFunction.CosineSimilarity or DistanceFunction.CosineDistance => "COS",
        DistanceFunction.EuclideanDistance or DistanceFunction.EuclideanSquaredDistance => "L2",
        DistanceFunction.DotProductSimilarity or DistanceFunction.NegativeDotProductSimilarity => "IP",
        _ => throw new NotSupportedException($"DocumentDB does not support distance function '{distanceFunction}'."),
    };
}
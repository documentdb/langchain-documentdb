using DocumentDB.LangChain;
using Microsoft.Extensions.VectorData;
using MongoDB.Driver;

namespace Examples;

public static class VectorStoreExample
{
    public static async Task<IReadOnlyList<VectorSearchResult<KnowledgeRecord>>> RunAsync(
        IMongoDatabase database,
        ReadOnlyMemory<float> documentEmbedding,
        ReadOnlyMemory<float> queryEmbedding,
        CancellationToken cancellationToken = default)
    {
        var store = new DocumentDBVectorStore(database, new DocumentDBVectorStoreOptions
        {
            DiskAnnMaxDegree = 32,
            DiskAnnBuildCandidates = 50,
            DiskAnnSearchCandidates = 80,
        });
        VectorStoreCollection<string, KnowledgeRecord> collection =
            store.GetCollection<string, KnowledgeRecord>("documents");

        await collection.EnsureCollectionExistsAsync(cancellationToken);
        await collection.UpsertAsync(new KnowledgeRecord
        {
            Id = "documentdb-overview",
            Text = "DocumentDB provides a MongoDB-compatible API.",
            Category = "database",
            Embedding = documentEmbedding,
        }, cancellationToken);

        var results = new List<VectorSearchResult<KnowledgeRecord>>();
        var options = new Microsoft.Extensions.VectorData.VectorSearchOptions<KnowledgeRecord>
        {
            Filter = record => record.Category == "database",
            ScoreThreshold = 0.75,
        };
        await foreach (var result in collection.SearchAsync(queryEmbedding, 4, options, cancellationToken))
        {
            results.Add(result);
        }
        return results;
    }

    public sealed class KnowledgeRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(StorageName = "content")]
        public string Text { get; set; } = string.Empty;

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = string.Empty;

        [VectorStoreVector(
            1536,
            StorageName = "embedding",
            IndexKind = IndexKind.DiskAnn,
            DistanceFunction = DistanceFunction.CosineSimilarity)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }
}
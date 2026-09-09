using Microsoft.Extensions.VectorData;
using MongoDB.Bson;

namespace DocumentDB.LangChain.Tests;

public sealed class DocumentDBCommandBuilderTests
{
    public static TheoryData<string, string, string> IndexCases => new()
    {
        { IndexKind.IvfFlat, "vector-ivf", "numLists" },
        { IndexKind.Hnsw, "vector-hnsw", "efConstruction" },
        { IndexKind.DiskAnn, "vector-diskann", "lBuild" },
    };

    [Theory]
    [MemberData(nameof(IndexCases))]
    public void CreateVectorIndex_UsesDocumentDBCommand(
        string indexKind,
        string expectedKind,
        string tuningField)
    {
        var vector = CreateVector(indexKind);

        var command = DocumentDBCommandBuilder.CreateVectorIndex(
            "documents",
            vector,
            new DocumentDBVectorStoreOptions());

        var index = command["indexes"].AsBsonArray[0].AsBsonDocument;
        Assert.Equal("documents", command["createIndexes"].AsString);
        Assert.Equal("content_vector_vector", index["name"].AsString);
        Assert.Equal("cosmosSearch", index["key"]["content.vector"].AsString);
        Assert.Equal(expectedKind, index["cosmosSearchOptions"]["kind"].AsString);
        Assert.True(index["cosmosSearchOptions"].AsBsonDocument.Contains(tuningField));
    }

    [Fact]
    public void CreateSearchPipeline_ProjectsScoreAndAppliesFilterThresholdAndSkip()
    {
        var vector = CreateVector(IndexKind.DiskAnn);
        var filter = new BsonDocument("category", new BsonDocument("$eq", "database"));

        var pipeline = DocumentDBCommandBuilder.CreateSearchPipeline(
            vector,
            new float[] { 0.1F, 0.2F, 0.3F },
            top: 4,
            skip: 2,
            scoreThreshold: 0.75,
            filter,
            new DocumentDBVectorStoreOptions { DiskAnnSearchCandidates = 80 });

        var search = pipeline[0]["$search"]["cosmosSearch"].AsBsonDocument;
        Assert.Equal(6, search["k"].AsInt32);
        Assert.Equal(80, search["lSearch"].AsInt32);
        Assert.Equal(filter, search["filter"].AsBsonDocument);
        Assert.Equal("searchScore", pipeline[1]["$project"]["similarityScore"]["$meta"].AsString);
        Assert.Equal(0.75, pipeline[2]["$match"]["similarityScore"]["$gte"].AsDouble);
        Assert.Equal(2, pipeline[3]["$skip"].AsInt32);
        Assert.Equal(4, pipeline[4]["$limit"].AsInt32);
    }

    [Fact]
    public void CreateSearchPipeline_UsesIndexSpecificSearchTuning()
    {
        var options = new DocumentDBVectorStoreOptions
        {
            IvfNumProbes = 3,
            HnswEfSearch = 75,
        };

        var ivf = DocumentDBCommandBuilder.CreateSearchPipeline(
            CreateVector(IndexKind.IvfFlat), new float[] { 1 }, 1, 0, null, null, options);
        var hnsw = DocumentDBCommandBuilder.CreateSearchPipeline(
            CreateVector(IndexKind.Hnsw), new float[] { 1 }, 1, 0, null, null, options);

        Assert.Equal(3, ivf[0]["$search"]["cosmosSearch"]["nProbes"].AsInt32);
        Assert.Equal(75, hnsw[0]["$search"]["cosmosSearch"]["efSearch"].AsInt32);
    }

    [Fact]
    public void Builders_RejectInvalidVectorsAndUnsupportedConfiguration()
    {
        var options = new DocumentDBVectorStoreOptions();
        Assert.Throws<ArgumentException>(() => DocumentDBCommandBuilder.CreateSearchPipeline(
            CreateVector(IndexKind.DiskAnn), ReadOnlyMemory<float>.Empty, 1, 0, null, null, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentDBCommandBuilder.CreateSearchPipeline(
            CreateVector(IndexKind.DiskAnn), new float[] { 1 }, 0, 0, null, null, options));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VectorStoreVectorProperty("Embedding", 0));

        var unsupported = new VectorMapping(
            "Embedding",
            "embedding",
            new VectorStoreVectorProperty("Embedding", 3) { IndexKind = IndexKind.Flat });
        Assert.Throws<NotSupportedException>(() => DocumentDBCommandBuilder.CreateVectorIndex(
            "documents", unsupported, options));
    }

    private static VectorMapping CreateVector(string indexKind) => new(
        "Embedding",
        "content.vector",
        new VectorStoreVectorProperty("Embedding", typeof(ReadOnlyMemory<float>), 3)
        {
            DistanceFunction = DistanceFunction.CosineSimilarity,
            IndexKind = indexKind,
            StorageName = "content.vector",
        });
}
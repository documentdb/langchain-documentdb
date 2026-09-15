using Microsoft.Extensions.VectorData;
using MongoDB.Driver;

namespace DocumentDB.LangChain.Tests;

public sealed class DocumentDBVectorStoreTests
{
    [Fact]
    public void GetCollection_ImplementsMevaAndRequiresStringKeys()
    {
        var database = new MongoClient().GetDatabase("documentdb_tests");
        var store = new DocumentDBVectorStore(database);

        var collection = store.GetCollection<string, DocumentDBRecordMapperTests.TestRecord>("documents");

        Assert.Equal("documents", collection.Name);
        Assert.Same(store, store.GetService(typeof(DocumentDBVectorStore)));
        Assert.Same(database, store.GetService(typeof(IMongoDatabase)));
        Assert.Throws<NotSupportedException>(() =>
            store.GetCollection<int, DocumentDBRecordMapperTests.TestRecord>("documents"));
        Assert.Throws<NotSupportedException>(() =>
            store.GetDynamicCollection("documents", new VectorStoreCollectionDefinition()));
    }

    [Theory]
    [InlineData(0, 1, 16, 64, 40, 32, 50, 40)]
    [InlineData(1, 0, 16, 64, 40, 32, 50, 40)]
    [InlineData(1, 1, 1, 64, 40, 32, 50, 40)]
    [InlineData(1, 1, 16, 20, 40, 32, 50, 40)]
    [InlineData(1, 1, 16, 64, 40, 19, 50, 40)]
    [InlineData(1, 1, 16, 64, 40, 32, 9, 40)]
    [InlineData(1, 1, 16, 64, 40, 32, 50, 9)]
    public void Constructor_RejectsInvalidOptions(
        int lists,
        int probes,
        int hnswM,
        int efConstruction,
        int efSearch,
        int maxDegree,
        int buildCandidates,
        int searchCandidates)
    {
        var database = new MongoClient().GetDatabase("documentdb_tests");
        var options = new DocumentDBVectorStoreOptions
        {
            IvfNumLists = lists,
            IvfNumProbes = probes,
            HnswM = hnswM,
            HnswEfConstruction = efConstruction,
            HnswEfSearch = efSearch,
            DiskAnnMaxDegree = maxDegree,
            DiskAnnBuildCandidates = buildCandidates,
            DiskAnnSearchCandidates = searchCandidates,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentDBVectorStore(database, options));
    }
}
using Microsoft.Extensions.VectorData;
using MongoDB.Bson;

namespace DocumentDB.LangChain.Tests;

public sealed class DocumentDBRecordMapperTests
{
    [Fact]
    public void RoundTrip_UsesDocumentDBStorageShape()
    {
        var mapper = new DocumentDBRecordMapper<TestRecord>(definition: null);
        var record = new TestRecord
        {
            Id = "record-1",
            Text = "DocumentDB",
            Category = "database",
            Embedding = new float[] { 0.1F, 0.2F, 0.3F },
        };

        var document = mapper.ToBsonDocument(record);

        Assert.Equal("record-1", document["_id"].AsString);
        Assert.Equal("DocumentDB", document["content"].AsString);
        Assert.Equal("database", document["category"].AsString);
        Assert.Equal(3, document["vector"].AsBsonArray.Count);

        var withoutVector = mapper.FromBsonDocument(document, includeVectors: false);
        var withVector = mapper.FromBsonDocument(document, includeVectors: true);
        Assert.Equal("record-1", withoutVector.Id);
        Assert.True(withoutVector.Embedding.IsEmpty);
        Assert.Equal(record.Embedding.ToArray(), withVector.Embedding.ToArray());
    }

    [Fact]
    public void RenderFilter_UsesStorageNames()
    {
        var mapper = new DocumentDBRecordMapper<TestRecord>(definition: null);

        var filter = mapper.RenderFilter(record => record.Category == "database");

        Assert.Equal("database", filter["category"].AsString);
        Assert.Equal("_id", mapper.GetStorageName(record => record.Id));
        Assert.Equal("content", mapper.GetStorageName(record => record.Text));
        Assert.Throws<ArgumentException>(() => mapper.GetStorageName(record => record.Text.Length));
    }

    [Fact]
    public void Constructor_RejectsMissingVectorAndNonStringKey()
    {
        Assert.Throws<ArgumentException>(() => new DocumentDBRecordMapper<NoVectorRecord>(definition: null));
        Assert.Throws<NotSupportedException>(() => new DocumentDBRecordMapper<NumericKeyRecord>(definition: null));
    }

    [Fact]
    public void GetVector_RequiresSelectorForMultipleVectors()
    {
        var mapper = new DocumentDBRecordMapper<MultipleVectorRecord>(definition: null);

        Assert.Throws<ArgumentException>(() => mapper.GetVector(selector: null));
        Assert.Equal("Secondary", mapper.GetVector(record => record.Secondary).PropertyName);
        Assert.Throws<ArgumentException>(() => mapper.GetVector(record => record.Text));
    }

    internal sealed class TestRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(StorageName = "content")]
        public string Text { get; set; } = string.Empty;

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = string.Empty;

        [VectorStoreVector(3, StorageName = "vector", IndexKind = IndexKind.DiskAnn)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }

    private sealed class NoVectorRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class NumericKeyRecord
    {
        [VectorStoreKey]
        public int Id { get; set; }

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }

    private sealed class MultipleVectorRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public string Text { get; set; } = string.Empty;

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Primary { get; set; }

        [VectorStoreVector(3)]
        public ReadOnlyMemory<float> Secondary { get; set; }
    }
}
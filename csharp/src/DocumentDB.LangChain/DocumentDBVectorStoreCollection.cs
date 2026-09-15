using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.VectorData;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DocumentDB.LangChain;

internal sealed class DocumentDBVectorStoreCollection<TRecord>(
    IMongoDatabase database,
    string name,
    VectorStoreCollectionDefinition? definition,
    DocumentDBVectorStoreOptions options)
    : VectorStoreCollection<string, TRecord>
    where TRecord : class
{
    private readonly IMongoDatabase _database = database;
    private readonly IMongoCollection<BsonDocument> _collection = database.GetCollection<BsonDocument>(name);
    private readonly DocumentDBRecordMapper<TRecord> _mapper = new(definition);
    private readonly DocumentDBVectorStoreOptions _options = options;

    public override string Name { get; } = name;

    public override async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        using var cursor = await _database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = new BsonDocument("name", Name) },
            cancellationToken).ConfigureAwait(false);
        return await cursor.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        if (!await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await _database.CreateCollectionAsync(Name, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var existingIndexes = new HashSet<string>(StringComparer.Ordinal);
        using (var cursor = await _collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var index in cursor.Current)
                {
                    if (index.TryGetValue("name", out var indexName) && indexName.IsString)
                    {
                        existingIndexes.Add(indexName.AsString);
                    }
                }
            }
        }

        foreach (var vector in _mapper.Vectors)
        {
            if (!existingIndexes.Contains(DocumentDBCommandBuilder.GetIndexName(vector.StorageName)))
            {
                var command = DocumentDBCommandBuilder.CreateVectorIndex(Name, vector, _options);
                await _database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.DropCollectionAsync(Name, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoCommandException exception) when (exception.Code == 26)
        {
        }
    }

    public override Task<TRecord?> GetAsync(
        string key,
        RecordRetrievalOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GetCoreAsync(key, options, cancellationToken);
    }

    public override async IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);
        var rendered = _mapper.RenderFilter(filter);
        var orderBy = options?.OrderBy?.Invoke(new FilteredRecordRetrievalOptions<TRecord>.OrderByDefinition());
        var sort = orderBy?.Values.Count > 0
            ? new BsonDocument(orderBy.Values.Select(value =>
                new BsonElement(_mapper.GetStorageName(value.PropertySelector), value.Ascending ? 1 : -1)))
            : null;
        using var cursor = await _collection.FindAsync(
            rendered,
            new FindOptions<BsonDocument>
            {
                Limit = top,
                Skip = options?.Skip,
                Sort = sort,
            },
            cancellationToken).ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                yield return _mapper.FromBsonDocument(document, options?.IncludeVectors ?? false);
            }
        }
    }

    public override Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _collection.DeleteOneAsync(new BsonDocument("_id", key), cancellationToken);
    }

    public override Task UpsertAsync(
        TRecord record,
        CancellationToken cancellationToken = default)
    {
        var document = _mapper.ToBsonDocument(record);
        return _collection.ReplaceOneAsync(
            new BsonDocument("_id", document["_id"]),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public override Task UpsertAsync(
        IEnumerable<TRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        var operations = records.Select(record =>
        {
            var document = _mapper.ToBsonDocument(record);
            return (WriteModel<BsonDocument>)new ReplaceOneModel<BsonDocument>(
                new BsonDocument("_id", document["_id"]),
                document)
            {
                IsUpsert = true,
            };
        }).ToArray();

        return operations.Length == 0
            ? Task.CompletedTask
            : _collection.BulkWriteAsync(operations, cancellationToken: cancellationToken);
    }

    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput vector,
        int top,
        Microsoft.Extensions.VectorData.VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = GetQueryVector(vector);
        var vectorMapping = _mapper.GetVector(options?.VectorProperty);
        var filter = options?.Filter is null ? null : _mapper.RenderFilter(options.Filter);
        var pipeline = DocumentDBCommandBuilder.CreateSearchPipeline(
            vectorMapping,
            query,
            top,
            options?.Skip ?? 0,
            options?.ScoreThreshold,
            filter,
            _options);
        using var cursor = await _collection.AggregateAsync<BsonDocument>(pipeline, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var result in cursor.Current)
            {
                var record = _mapper.FromBsonDocument(result["document"].AsBsonDocument, options?.IncludeVectors ?? false);
                yield return new VectorSearchResult<TRecord>(record, result["similarityScore"].ToDouble());
            }
        }
    }

    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null
            ? null
            : serviceType.IsInstanceOfType(this)
                ? this
                : serviceType.IsInstanceOfType(_collection)
                    ? _collection
                    : null;

    private async Task<TRecord?> GetCoreAsync(
        string key,
        RecordRetrievalOptions? options,
        CancellationToken cancellationToken)
    {
        var document = await _collection.Find(new BsonDocument("_id", key))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return document is null
            ? null
            : _mapper.FromBsonDocument(document, options?.IncludeVectors ?? false);
    }

    private static ReadOnlyMemory<float> GetQueryVector<TInput>(TInput vector) => vector switch
    {
        ReadOnlyMemory<float> memory => memory,
        Memory<float> memory => memory,
        float[] array => array,
        IEnumerable<float> values => values.ToArray(),
        _ => throw new NotSupportedException(
            $"Query vectors of type '{typeof(TInput).Name}' are not supported. Use float values."),
    };
}
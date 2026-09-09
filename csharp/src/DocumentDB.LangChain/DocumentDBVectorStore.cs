using System.Runtime.CompilerServices;
using Microsoft.Extensions.VectorData;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DocumentDB.LangChain;

/// <summary>Provides MEVA vector collections backed by DocumentDB.</summary>
public sealed class DocumentDBVectorStore : VectorStore
{
    private readonly DocumentDBVectorStoreOptions _options;

    /// <summary>Initializes a DocumentDB vector store.</summary>
    public DocumentDBVectorStore(
        IMongoDatabase database,
        DocumentDBVectorStoreOptions? options = null)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? new DocumentDBVectorStoreOptions();
        _options.Validate();
    }

    /// <summary>Gets the caller-configured MongoDB-compatible database.</summary>
    public IMongoDatabase Database { get; }

    /// <inheritdoc />
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition? definition = null)
    {
        if (typeof(TKey) != typeof(string))
        {
            throw new NotSupportedException("DocumentDB vector collections require string keys.");
        }

        return (VectorStoreCollection<TKey, TRecord>)(object)new DocumentDBVectorStoreCollection<TRecord>(
            Database,
            name,
            definition,
            _options);
    }

    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name,
        VectorStoreCollectionDefinition definition) =>
        throw new NotSupportedException("Dynamic record mapping is not supported.");

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var cursor = await Database.ListCollectionNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var name in cursor.Current)
            {
                yield return name;
            }
        }
    }

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(
        string name,
        CancellationToken cancellationToken = default) => CollectionExistsCoreAsync(name, cancellationToken);

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            await Database.DropCollectionAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoCommandException exception) when (exception.Code == 26)
        {
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null
            ? null
            : serviceType.IsInstanceOfType(this)
                ? this
                : serviceType.IsInstanceOfType(Database)
                    ? Database
                    : null;

    private async Task<bool> CollectionExistsCoreAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var cursor = await Database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = new BsonDocument("name", name) },
            cancellationToken).ConfigureAwait(false);
        return await cursor.AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}
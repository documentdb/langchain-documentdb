using Microsoft.Extensions.VectorData;
using MongoDB.Driver;
using MongoDB.Driver.Authentication.Oidc;

namespace DocumentDB.LangChain.Tests;

public sealed class DocumentDBIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CrudAndVectorSearch_WorkAgainstDocumentDB()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DOCUMENTDB_RUN_INTEGRATION_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var databaseName = Environment.GetEnvironmentVariable("DOCUMENTDB_DATABASE")
            ?? throw new InvalidOperationException("DOCUMENTDB_DATABASE is required.");
        var collectionPrefix = Environment.GetEnvironmentVariable("DOCUMENTDB_COLLECTION") ?? "langchain_tests";
        var collectionName = $"{collectionPrefix}_{Guid.NewGuid():N}";

        using var client = CreateClient();
        var store = new DocumentDBVectorStore(client.GetDatabase(databaseName));
        var collection = store.GetCollection<string, IntegrationRecord>(collectionName);

        try
        {
            await collection.EnsureCollectionExistsAsync();
            await collection.UpsertAsync(new IntegrationRecord
            {
                Id = "one",
                Text = "DocumentDB vector search",
                Category = "database",
                Embedding = new float[] { 1, 0, 0 },
            });

            var stored = await collection.GetAsync("one", new RecordRetrievalOptions { IncludeVectors = true });
            Assert.NotNull(stored);
            Assert.Equal("DocumentDB vector search", stored.Text);

            var results = await collection.SearchAsync(new float[] { 1, 0, 0 }, top: 1).ToListAsync();
            Assert.Single(results);
            Assert.Equal("one", results[0].Record.Id);
        }
        finally
        {
            await collection.EnsureCollectionDeletedAsync();
        }
    }

    private static MongoClient CreateClient()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("DOCUMENTDB_AUTH_MODE"), "entra", StringComparison.Ordinal))
        {
            var endpoint = Environment.GetEnvironmentVariable("DOCUMENTDB_ENDPOINT")
                ?? throw new InvalidOperationException("DOCUMENTDB_ENDPOINT is required.");
            var accessToken = Environment.GetEnvironmentVariable("DOCUMENTDB_OIDC_TOKEN")
                ?? throw new InvalidOperationException("DOCUMENTDB_OIDC_TOKEN is required.");
            var settings = MongoClientSettings.FromConnectionString(endpoint);
            settings.UseTls = true;
            settings.RetryWrites = false;
            settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(2);
            settings.Credential = MongoCredential.CreateOidcCredential(new StaticOidcCallback(accessToken));
            return new MongoClient(settings);
        }

        Console.Error.WriteLine("WARNING: integration-test-only connection string authentication is enabled.");
        var connectionString = Environment.GetEnvironmentVariable("DOCUMENTDB_CONNECTION_STRING")
            ?? throw new InvalidOperationException("DOCUMENTDB_CONNECTION_STRING is required.");
        return new MongoClient(connectionString);
    }

    private sealed class StaticOidcCallback(string accessToken) : IOidcCallback
    {
        public OidcAccessToken GetOidcAccessToken(OidcCallbackParameters parameters, CancellationToken cancellationToken)
        {
            return new OidcAccessToken(accessToken, TimeSpan.FromMinutes(30));
        }

        public Task<OidcAccessToken> GetOidcAccessTokenAsync(OidcCallbackParameters parameters, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetOidcAccessToken(parameters, cancellationToken));
        }
    }

    private sealed class IntegrationRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData(StorageName = "content")]
        public string Text { get; set; } = string.Empty;

        [VectorStoreData(StorageName = "category")]
        public string Category { get; set; } = string.Empty;

        [VectorStoreVector(3, StorageName = "vector", IndexKind = IndexKind.IvfFlat)]
        public ReadOnlyMemory<float> Embedding { get; set; }
    }
}
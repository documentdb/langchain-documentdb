using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.VectorData;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace DocumentDB.LangChain;

internal sealed class DocumentDBRecordMapper<TRecord>
    where TRecord : class
{
    private readonly IReadOnlyList<PropertyMapping> _properties;
    private readonly PropertyMapping _key;
    private readonly IReadOnlyList<PropertyMapping> _vectors;

    public DocumentDBRecordMapper(VectorStoreCollectionDefinition? definition)
    {
        _properties = BuildMappings(definition);
        _key = _properties.SingleOrDefault(static property => property.Definition is VectorStoreKeyProperty)
            ?? throw new ArgumentException("The record must define exactly one vector store key property.", nameof(definition));
        _vectors = _properties.Where(static property => property.Definition is VectorStoreVectorProperty).ToArray();

        if (_key.Property.PropertyType != typeof(string))
        {
            throw new NotSupportedException("DocumentDB vector collection keys must be strings.");
        }

        if (_vectors.Count == 0)
        {
            throw new ArgumentException("The record must define at least one vector property.", nameof(definition));
        }
    }

    public IReadOnlyList<VectorMapping> Vectors => _vectors
        .Select(static property => ToVectorMapping(property))
        .ToArray();

    public BsonDocument ToBsonDocument(TRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var serialized = record.ToBsonDocument();
        var document = new BsonDocument();

        foreach (var mapping in _properties)
        {
            if (serialized.TryGetValue(mapping.SerializedName, out var value))
            {
                document[mapping == _key ? "_id" : mapping.StorageName] = value.DeepClone();
            }
        }

        if (!document.TryGetValue("_id", out var key) || !key.IsString || string.IsNullOrWhiteSpace(key.AsString))
        {
            throw new ArgumentException("The vector store key must be a non-empty string.", nameof(record));
        }

        return document;
    }

    public TRecord FromBsonDocument(BsonDocument document, bool includeVectors)
    {
        var serialized = new BsonDocument();
        foreach (var mapping in _properties)
        {
            if (!includeVectors && mapping.Definition is VectorStoreVectorProperty)
            {
                continue;
            }

            var storageName = mapping == _key ? "_id" : mapping.StorageName;
            if (document.TryGetValue(storageName, out var value))
            {
                serialized[mapping.SerializedName] = value.DeepClone();
            }
        }

        return BsonSerializer.Deserialize<TRecord>(serialized);
    }

    public VectorMapping GetVector(Expression<Func<TRecord, object?>>? selector)
    {
        if (selector is null)
        {
            return _vectors.Count == 1
                ? ToVectorMapping(_vectors[0])
                : throw new ArgumentException("VectorProperty is required when a record has multiple vectors.", nameof(selector));
        }

        var expression = selector.Body is UnaryExpression unary ? unary.Operand : selector.Body;
        if (expression is not MemberExpression member)
        {
            throw new ArgumentException("VectorProperty must select a record property.", nameof(selector));
        }

        var mapping = _vectors.SingleOrDefault(property => property.Property == member.Member)
            ?? throw new ArgumentException("VectorProperty must select a configured vector property.", nameof(selector));
        return ToVectorMapping(mapping);
    }

    public BsonDocument RenderFilter(Expression<Func<TRecord, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var serializer = BsonSerializer.LookupSerializer<TRecord>();
        var rendered = MongoDB.Driver.Builders<TRecord>.Filter
            .Where(filter)
            .Render(new MongoDB.Driver.RenderArgs<TRecord>(serializer, BsonSerializer.SerializerRegistry));
        return RenameFilterFields(rendered);
    }

    public string GetStorageName(Expression<Func<TRecord, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var expression = selector.Body is UnaryExpression unary ? unary.Operand : selector.Body;
        if (expression is not MemberExpression member)
        {
            throw new ArgumentException("The selector must select a record property.", nameof(selector));
        }

        var mapping = _properties.SingleOrDefault(property => property.Property == member.Member)
            ?? throw new ArgumentException("The selector must select a configured record property.", nameof(selector));
        return mapping == _key ? "_id" : mapping.StorageName;
    }

    private BsonDocument RenameFilterFields(BsonDocument filter)
    {
        var result = new BsonDocument();
        foreach (var element in filter)
        {
            var name = element.Name;
            if (!name.StartsWith('$'))
            {
                var mapping = _properties.FirstOrDefault(property =>
                    name == property.SerializedName || name.StartsWith($"{property.SerializedName}.", StringComparison.Ordinal));
                if (mapping is not null)
                {
                    name = (mapping == _key ? "_id" : mapping.StorageName) + name[mapping.SerializedName.Length..];
                }
            }

            result[name] = element.Value is BsonDocument nested ? RenameFilterFields(nested) : element.Value.DeepClone();
        }

        return result;
    }

    private static VectorMapping ToVectorMapping(PropertyMapping mapping) =>
        new(mapping.Property.Name, mapping.StorageName, (VectorStoreVectorProperty)mapping.Definition);

    private static IReadOnlyList<PropertyMapping> BuildMappings(VectorStoreCollectionDefinition? definition)
    {
        var recordType = typeof(TRecord);
        var classMap = BsonClassMap.LookupClassMap(recordType);
        var definitions = definition?.Properties ?? BuildDefinitionFromAttributes(recordType);
        var mappings = new List<PropertyMapping>(definitions.Count);

        foreach (var propertyDefinition in definitions)
        {
            var property = recordType.GetProperty(propertyDefinition.Name, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new ArgumentException(
                    $"Property '{propertyDefinition.Name}' does not exist on {recordType.Name}.",
                    nameof(definition));
            var memberMap = classMap.GetMemberMap(property.Name)
                ?? throw new ArgumentException($"Property '{property.Name}' is not BSON serializable.", nameof(definition));
            var storageName = propertyDefinition is VectorStoreKeyProperty
                ? "_id"
                : propertyDefinition.StorageName ?? memberMap.ElementName;
            mappings.Add(new PropertyMapping(property, memberMap.ElementName, storageName, propertyDefinition));
        }

        return mappings;
    }

    private static IList<VectorStoreProperty> BuildDefinitionFromAttributes(Type recordType)
    {
        var definitions = new List<VectorStoreProperty>();
        foreach (var property in recordType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetCustomAttribute<VectorStoreKeyAttribute>() is { } key)
            {
                definitions.Add(new VectorStoreKeyProperty(property.Name, property.PropertyType)
                {
                    IsAutoGenerated = key.IsAutoGenerated,
                    StorageName = key.StorageName,
                });
            }
            else if (property.GetCustomAttribute<VectorStoreVectorAttribute>() is { } vector)
            {
                definitions.Add(new VectorStoreVectorProperty(property.Name, property.PropertyType, vector.Dimensions)
                {
                    DistanceFunction = vector.DistanceFunction,
                    IndexKind = vector.IndexKind,
                    StorageName = vector.StorageName,
                });
            }
            else if (property.GetCustomAttribute<VectorStoreDataAttribute>() is { } data)
            {
                definitions.Add(new VectorStoreDataProperty(property.Name, property.PropertyType)
                {
                    IsFullTextIndexed = data.IsFullTextIndexed,
                    IsIndexed = data.IsIndexed,
                    StorageName = data.StorageName,
                });
            }
        }

        return definitions;
    }

    private sealed record PropertyMapping(
        PropertyInfo Property,
        string SerializedName,
        string StorageName,
        VectorStoreProperty Definition);
}

internal sealed record VectorMapping(
    string PropertyName,
    string StorageName,
    VectorStoreVectorProperty Definition);
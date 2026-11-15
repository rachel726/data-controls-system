using System.Diagnostics.CodeAnalysis;

namespace DataControls.Core.Catalog;

/// <summary>
/// Immutable snapshot of the business catalog with fast lookups.
/// Construction verifies the catalog's internal consistency, so the rest of the engine
/// can trust that every relation target, display field and key is valid.
/// </summary>
public sealed class MetadataCatalog
{
    private readonly Dictionary<string, EntityMeta> _entities;
    private readonly Dictionary<(string Entity, string Field), FieldMeta> _fields;
    private readonly Dictionary<(string Entity, string Relation), RelationMeta> _relations;

    public MetadataCatalog(IEnumerable<EntityMeta> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        Entities = [.. entities.OrderBy(e => e.SortOrder).ThenBy(e => e.Key, StringComparer.Ordinal)];
        _entities = new(StringComparer.Ordinal);
        _fields = [];
        _relations = [];

        foreach (var entity in Entities)
        {
            if (!_entities.TryAdd(entity.Key, entity))
            {
                throw new CatalogException($"Duplicate entity key '{entity.Key}'.");
            }

            foreach (var field in entity.Fields)
            {
                if (!_fields.TryAdd((entity.Key, field.Key), field))
                {
                    throw new CatalogException($"Duplicate field key '{entity.Key}.{field.Key}'.");
                }
            }

            foreach (var relation in entity.Relations)
            {
                if (relation.FromEntityKey != entity.Key)
                {
                    throw new CatalogException($"Relation '{entity.Key}.{relation.Key}' is declared on the wrong entity ('{relation.FromEntityKey}').");
                }

                if (_fields.ContainsKey((entity.Key, relation.Key)))
                {
                    throw new CatalogException($"Key '{entity.Key}.{relation.Key}' is both a field and a relation; field paths would be ambiguous.");
                }

                if (!_relations.TryAdd((entity.Key, relation.Key), relation))
                {
                    throw new CatalogException($"Duplicate relation key '{entity.Key}.{relation.Key}'.");
                }
            }
        }

        foreach (var entity in Entities)
        {
            if (!_fields.ContainsKey((entity.Key, entity.DisplayFieldKey)))
            {
                throw new CatalogException($"Entity '{entity.Key}' display field '{entity.DisplayFieldKey}' does not exist.");
            }

            foreach (var relation in entity.Relations.Where(r => !_entities.ContainsKey(r.ToEntityKey)))
            {
                throw new CatalogException($"Relation '{entity.Key}.{relation.Key}' points to unknown entity '{relation.ToEntityKey}'.");
            }
        }
    }

    /// <summary>All entities, ordered by SortOrder.</summary>
    public IReadOnlyList<EntityMeta> Entities { get; }

    public bool TryGetEntity(string key, [NotNullWhen(true)] out EntityMeta? entity) =>
        _entities.TryGetValue(key, out entity);

    /// <exception cref="KeyNotFoundException">The entity does not exist.</exception>
    public EntityMeta GetEntity(string key) =>
        _entities.TryGetValue(key, out var entity)
            ? entity
            : throw new KeyNotFoundException($"Unknown entity '{key}'.");

    public bool TryGetField(string entityKey, string fieldKey, [NotNullWhen(true)] out FieldMeta? field) =>
        _fields.TryGetValue((entityKey, fieldKey), out field);

    public bool TryGetRelation(string entityKey, string relationKey, [NotNullWhen(true)] out RelationMeta? relation) =>
        _relations.TryGetValue((entityKey, relationKey), out relation);
}

/// <summary>The catalog is internally inconsistent (a configuration error, not a user error).</summary>
public sealed class CatalogException(string message) : Exception(message);

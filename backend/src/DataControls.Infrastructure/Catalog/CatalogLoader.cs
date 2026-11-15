using System.Text.Json;
using Dapper;
using DataControls.Core.Catalog;
using DataControls.Infrastructure.Data;

namespace DataControls.Infrastructure.Catalog;

/// <summary>Loads the business catalog (meta.Entities / Fields / Relations).</summary>
public interface ICatalogLoader
{
    Task<MetadataCatalog> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the three catalog tables in one round trip and builds an immutable <see cref="MetadataCatalog"/>
/// (whose constructor verifies the catalog's consistency).
/// </summary>
public sealed class CatalogLoader(ISqlConnectionFactory connections) : ICatalogLoader
{
    private const string Sql = """
        SELECT Id, [Key], DisplayName, DisplayNamePlural, Description, SchemaName, TableName, PrimaryKeyColumn, DisplayFieldKey, SortOrder, Gender
        FROM meta.Entities;

        SELECT EntityId, [Key], DisplayName, Description, SourceColumn, DataType, Transform, TransformArg,
               IsRequiredByBusiness, IsVisibleInResults, IsSearchable, AllowedValuesJson, Unit, SortOrder, TrueText, FalseText
        FROM meta.Fields
        ORDER BY EntityId, SortOrder, [Key];

        SELECT FromEntityId, ToEntityId, [Key], FromColumn, ToColumn, Cardinality, DisplayName, Description
        FROM meta.Relations
        ORDER BY FromEntityId, Id;
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MetadataCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(Sql, cancellationToken: cancellationToken));

        var entities = (await grid.ReadAsync<EntityRow>()).ToList();
        var fields = (await grid.ReadAsync<FieldRow>()).ToLookup(f => f.EntityId);
        var relations = (await grid.ReadAsync<RelationRow>()).ToLookup(r => r.FromEntityId);
        var keyById = entities.ToDictionary(e => e.Id, e => e.Key);

        return new MetadataCatalog(entities.Select(e => new EntityMeta
        {
            Key = e.Key,
            DisplayName = e.DisplayName,
            DisplayNamePlural = e.DisplayNamePlural,
            Gender = Enum.Parse<GrammaticalGender>(e.Gender),
            Description = e.Description,
            SchemaName = e.SchemaName,
            TableName = e.TableName,
            PrimaryKeyColumn = e.PrimaryKeyColumn,
            DisplayFieldKey = e.DisplayFieldKey,
            SortOrder = e.SortOrder,
            Fields = [.. fields[e.Id].Select(ToField)],
            Relations = [.. relations[e.Id].Select(r => ToRelation(r, keyById))],
        }));
    }

    private static FieldMeta ToField(FieldRow f) => new()
    {
        Key = f.Key,
        DisplayName = f.DisplayName,
        Description = f.Description,
        SourceColumn = f.SourceColumn,
        DataType = Enum.Parse<DataType>(f.DataType),
        Transform = Enum.Parse<FieldTransform>(f.Transform),
        TransformArg = f.TransformArg,
        IsRequiredByBusiness = f.IsRequiredByBusiness,
        IsVisibleInResults = f.IsVisibleInResults,
        IsSearchable = f.IsSearchable,
        AllowedValues = f.AllowedValuesJson is null
            ? []
            : JsonSerializer.Deserialize<List<AllowedValue>>(f.AllowedValuesJson, JsonOptions) ?? [],
        Unit = f.Unit,
        TrueText = f.TrueText,
        FalseText = f.FalseText,
        SortOrder = f.SortOrder,
    };

    private static RelationMeta ToRelation(RelationRow r, Dictionary<int, string> keyById) => new()
    {
        Key = r.Key,
        FromEntityKey = keyById[r.FromEntityId],
        ToEntityKey = keyById[r.ToEntityId],
        FromColumn = r.FromColumn,
        ToColumn = r.ToColumn,
        Cardinality = Enum.Parse<Cardinality>(r.Cardinality),
        DisplayName = r.DisplayName,
        Description = r.Description,
    };

    // Row shapes exactly as stored; mapped by Dapper by column name.
    private sealed record EntityRow(int Id, string Key, string DisplayName, string DisplayNamePlural, string? Description,
        string SchemaName, string TableName, string PrimaryKeyColumn, string DisplayFieldKey, int SortOrder, string Gender);

    private sealed record FieldRow(int EntityId, string Key, string DisplayName, string? Description, string SourceColumn,
        string DataType, string Transform, string? TransformArg, bool IsRequiredByBusiness, bool IsVisibleInResults,
        bool IsSearchable, string? AllowedValuesJson, string? Unit, int SortOrder, string? TrueText, string? FalseText);

    private sealed record RelationRow(int FromEntityId, int ToEntityId, string Key, string FromColumn, string ToColumn,
        string Cardinality, string DisplayName, string? Description);
}

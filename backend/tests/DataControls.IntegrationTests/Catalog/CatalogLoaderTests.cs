using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Planning;
using DataControls.Core.Sql;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using DataControls.Infrastructure.Catalog;
using DataControls.Infrastructure.Data;
using Shouldly;

namespace DataControls.IntegrationTests.Catalog;

public class CatalogLoaderTests
{
    private static Task<MetadataCatalog> Load() =>
        new CatalogLoader(new SqlConnectionFactory(TestDatabase.ConnectionString)).LoadAsync(CancellationToken.None);

    [Fact]
    public async Task Loads_the_seeded_catalog()
    {
        var catalog = await Load();

        catalog.Entities.Select(e => e.Key).ShouldBe(["employee", "department", "project"]);
        catalog.Entities.Sum(e => e.Fields.Count).ShouldBe(20);
        catalog.Entities.Sum(e => e.Relations.Count).ShouldBe(6);

        catalog.TryGetField("project", "status", out var status).ShouldBeTrue();
        status!.AllowedValues.ShouldContain(new AllowedValue("Cancelled", "בוטל"));
        catalog.TryGetField("employee", "fullName", out var fullName).ShouldBeTrue();
        (fullName!.Transform, fullName.TransformArg).ShouldBe((FieldTransform.FullName, "LastName"));
        catalog.TryGetRelation("department", "employees", out var employees).ShouldBeTrue();
        employees!.Cardinality.ShouldBe(Cardinality.OneToMany);
    }

    /// <summary>
    /// The in-memory TestCatalog used by all engine unit tests must mirror the database seed;
    /// this fails if someone changes one without the other. (Descriptions are not compared - wording only.)
    /// </summary>
    [Fact]
    public async Task Database_catalog_matches_the_test_catalog()
    {
        var loaded = await Load();

        Shape(loaded).ShouldBe(Shape(TestCatalog.Instance));
    }

    [Fact]
    public async Task Engine_works_end_to_end_with_the_loaded_catalog()
    {
        var catalog = await Load();
        var model = QueryPlanner.Plan(DefinitionSerializer.Deserialize(SampleDefinitions.ActiveMissingRequiredData), catalog, ValidationLimits.Default);

        var summary = (await TestDatabase.QueryAsync(new SqlServerRenderer().RenderSummary(model, DateOnly.FromDateTime(DateTime.Today)))).Single();

        summary[SqlServerRenderer.ViolationsColumn].ShouldBe(6L);
    }

    private static List<string> Shape(MetadataCatalog catalog) =>
    [
        .. catalog.Entities.Select(e =>
            $"E {e.Key}|{e.DisplayName}|{e.DisplayNamePlural}|{e.SchemaName}.{e.TableName}|{e.PrimaryKeyColumn}|{e.DisplayFieldKey}|{e.SortOrder}"),
        .. catalog.Entities.SelectMany(e => e.Fields.Select(f =>
            $"F {e.Key}.{f.Key}|{f.DisplayName}|{f.SourceColumn}|{f.DataType}|{f.Transform}|{f.TransformArg}|" +
            $"req={f.IsRequiredByBusiness}|vis={f.IsVisibleInResults}|search={f.IsSearchable}|{f.Unit}|" +
            string.Join(",", f.AllowedValues.Select(a => $"{a.Value}={a.Label}")))),
        .. catalog.Entities.SelectMany(e => e.Relations.Select(r =>
            $"R {r.FromEntityKey}.{r.Key}->{r.ToEntityKey}|{r.FromColumn}={r.ToColumn}|{r.Cardinality}|{r.DisplayName}")),
    ];
}

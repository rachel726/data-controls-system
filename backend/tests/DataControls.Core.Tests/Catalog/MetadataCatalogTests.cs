using DataControls.Core.Catalog;
using DataControls.Core.Tests.Fixtures;
using Shouldly;

namespace DataControls.Core.Tests.Catalog;

public class MetadataCatalogTests
{
    private static readonly MetadataCatalog Catalog = TestCatalog.Instance;

    [Fact]
    public void Test_catalog_mirrors_the_seed_counts()
    {
        Catalog.Entities.Select(e => e.Key).ShouldBe(["employee", "department", "project"]);
        Catalog.Entities.Sum(e => e.Fields.Count).ShouldBe(20);
        Catalog.Entities.Sum(e => e.Relations.Count).ShouldBe(6);
    }

    [Fact]
    public void Looks_up_entities_fields_and_relations_by_key()
    {
        Catalog.GetEntity("employee").DisplayName.ShouldBe("עובד");

        Catalog.TryGetField("employee", "age", out var age).ShouldBeTrue();
        age!.Transform.ShouldBe(FieldTransform.YearsSince);

        Catalog.TryGetRelation("employee", "department", out var relation).ShouldBeTrue();
        relation!.ToEntityKey.ShouldBe("department");
        relation.Cardinality.ShouldBe(Cardinality.ManyToOne);
    }

    [Fact]
    public void Same_key_on_different_entities_is_independent()
    {
        Catalog.TryGetField("employee", "isActive", out var employeeActive).ShouldBeTrue();
        Catalog.TryGetField("department", "isActive", out var departmentActive).ShouldBeTrue();

        employeeActive!.DisplayName.ShouldBe("עובד פעיל");
        departmentActive!.DisplayName.ShouldBe("מחלקה פעילה");
    }

    [Fact]
    public void Lookups_are_case_sensitive_and_do_not_throw_for_unknown_keys()
    {
        Catalog.TryGetEntity("Employee", out _).ShouldBeFalse();
        Catalog.TryGetField("employee", "Age", out _).ShouldBeFalse();
        Catalog.TryGetField("nosuch", "age", out _).ShouldBeFalse();
        Catalog.TryGetRelation("employee", "employees", out _).ShouldBeFalse();
        Should.Throw<KeyNotFoundException>(() => Catalog.GetEntity("nosuch"));
    }

    [Fact]
    public void Rejects_duplicate_entity_keys() =>
        Should.Throw<CatalogException>(() => new MetadataCatalog([Entity("a"), Entity("a")]))
            .Message.ShouldContain("Duplicate entity");

    [Fact]
    public void Rejects_duplicate_field_keys() =>
        Should.Throw<CatalogException>(() => new MetadataCatalog([Entity("a") with { Fields = [Field("x"), Field("x")] }]))
            .Message.ShouldContain("Duplicate field");

    [Fact]
    public void Rejects_relation_to_an_unknown_entity() =>
        Should.Throw<CatalogException>(() => new MetadataCatalog([Entity("a") with { Relations = [Relation("b", "a", "missing")] }]))
            .Message.ShouldContain("unknown entity");

    [Fact]
    public void Rejects_a_key_that_is_both_field_and_relation() =>
        Should.Throw<CatalogException>(() => new MetadataCatalog(
            [Entity("a") with { Fields = [Field("title"), Field("b")], Relations = [Relation("b", "a", "a")] }]))
            .Message.ShouldContain("ambiguous");

    [Fact]
    public void Rejects_a_missing_display_field() =>
        Should.Throw<CatalogException>(() => new MetadataCatalog([Entity("a") with { DisplayFieldKey = "missing" }]))
            .Message.ShouldContain("display field");

    [Fact]
    public void Rejects_a_relation_declared_on_the_wrong_entity() =>
        Should.Throw<CatalogException>(() => new MetadataCatalog(
            [Entity("a") with { Relations = [Relation("r", "b", "a")] }, Entity("b")]))
            .Message.ShouldContain("wrong entity");

    private static EntityMeta Entity(string key) => new()
    {
        Key = key, DisplayName = key, DisplayNamePlural = key, SchemaName = "s", TableName = key,
        PrimaryKeyColumn = "Id", DisplayFieldKey = "title", Fields = [Field("title")],
    };

    private static FieldMeta Field(string key) => new()
    {
        Key = key, DisplayName = key, SourceColumn = key, DataType = DataType.Text,
    };

    private static RelationMeta Relation(string key, string from, string to) => new()
    {
        Key = key, FromEntityKey = from, ToEntityKey = to, FromColumn = "Id", ToColumn = "Id",
        Cardinality = Cardinality.ManyToOne, DisplayName = key,
    };
}

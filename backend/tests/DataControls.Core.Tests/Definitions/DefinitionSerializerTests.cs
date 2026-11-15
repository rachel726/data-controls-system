using System.Text.Json;
using System.Text.Json.Nodes;
using DataControls.Core.Definitions;
using DataControls.Core.Tests.Fixtures;
using Shouldly;

namespace DataControls.Core.Tests.Definitions;

public class DefinitionSerializerTests
{
    [Theory]
    [MemberData(nameof(SampleDefinitions.All), MemberType = typeof(SampleDefinitions))]
    public void Sample_controls_round_trip_without_loss(string name, string json)
    {
        var definition = DefinitionSerializer.Deserialize(json);
        var serialized = DefinitionSerializer.Serialize(definition);

        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(serialized)).ShouldBeTrue(name);
    }

    [Fact]
    public void Deserializes_every_node_and_operand_kind_into_typed_objects()
    {
        var definition = DefinitionSerializer.Deserialize("""
            { "schemaVersion": 1, "rootEntity": "department",
              "filter": { "type": "group", "logic": "or", "children": [
                { "type": "condition", "field": "name", "operator": "contains", "operand": { "kind": "literal", "value": "פיתוח" } },
                { "type": "condition", "field": "code", "operator": "in", "operand": { "kind": "literal", "value": ["HR", "FIN"] } },
                { "type": "condition", "field": "isActive", "operator": "isNotEmpty" },
                { "type": "related", "relation": "projects", "quantifier": "any" },
                { "type": "group", "logic": "and", "children": [] } ] } }
            """);

        definition.SchemaVersion.ShouldBe(1);
        definition.RootEntity.ShouldBe("department");
        definition.Filter.Logic.ShouldBe(LogicalOperator.Or);
        definition.Filter.Children.Count.ShouldBe(5);

        var contains = definition.Filter.Children[0].ShouldBeOfType<ConditionNode>();
        contains.Operator.ShouldBe(ConditionOperator.Contains);
        contains.Operand.ShouldBeOfType<LiteralOperand>().Value.GetString().ShouldBe("פיתוח");

        var inList = definition.Filter.Children[1].ShouldBeOfType<ConditionNode>();
        inList.Operand.ShouldBeOfType<LiteralOperand>().Value.ValueKind.ShouldBe(JsonValueKind.Array);

        var noOperand = definition.Filter.Children[2].ShouldBeOfType<ConditionNode>();
        noOperand.Operator.ShouldBe(ConditionOperator.IsNotEmpty);
        noOperand.Operand.ShouldBeNull();

        var related = definition.Filter.Children[3].ShouldBeOfType<RelatedNode>();
        related.Quantifier.ShouldBe(RelatedQuantifier.Any);
        related.Where.ShouldBeNull();

        definition.Filter.Children[4].ShouldBeOfType<GroupNode>().Children.ShouldBeEmpty();
    }

    [Fact]
    public void Field_operand_and_related_where_are_typed()
    {
        var fieldToField = DefinitionSerializer.Deserialize(SampleDefinitions.EarnsMoreThanManager);
        fieldToField.Filter.Children[0].ShouldBeOfType<ConditionNode>()
            .Operand.ShouldBeOfType<FieldOperand>().Field.ShouldBe("manager.salary");

        var related = DefinitionSerializer.Deserialize(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees);
        var node = related.Filter.Children[1].ShouldBeOfType<RelatedNode>();
        node.Relation.ShouldBe("employees");
        node.Quantifier.ShouldBe(RelatedQuantifier.None);
        node.Where.ShouldNotBeNull().Children.Single().ShouldBeOfType<ConditionNode>().Field.ShouldBe("isActive");
    }

    [Fact]
    public void Writes_camelCase_strings_for_enums_and_omits_missing_operand()
    {
        var definition = new ControlDefinition(1, "employee",
            new GroupNode(LogicalOperator.And, [new ConditionNode("email", ConditionOperator.IsEmpty)]));

        var json = DefinitionSerializer.Serialize(definition);

        json.ShouldContain("\"logic\":\"and\"");
        json.ShouldContain("\"operator\":\"isEmpty\"");
        json.ShouldContain("\"type\":\"condition\"");
        json.ShouldNotContain("operand");
    }

    [Fact]
    public void Accepts_discriminator_that_is_not_the_first_property()
    {
        var definition = DefinitionSerializer.Deserialize("""
            { "rootEntity": "employee", "schemaVersion": 1,
              "filter": { "children": [ { "field": "age", "operator": "lt", "type": "condition",
                                          "operand": { "value": 18, "kind": "literal" } } ], "logic": "and", "type": "group" } }
            """);

        definition.Filter.Children.Single().ShouldBeOfType<ConditionNode>().Field.ShouldBe("age");
    }

    public static TheoryData<string, string> InvalidJson => new()
    {
        { "empty string", "" },
        { "whitespace", "   " },
        { "not JSON", "this is not json" },
        { "JSON null", "null" },
        { "array instead of object", "[]" },
        { "unknown node type", Wrap("""{ "type": "sql", "text": "DROP TABLE x" }""") },
        { "node without type", Wrap("""{ "field": "age", "operator": "lt" }""") },
        { "unknown operator", Wrap("""{ "type": "condition", "field": "age", "operator": "like" }""") },
        { "operator as number", Wrap("""{ "type": "condition", "field": "age", "operator": 4 }""") },
        { "unknown operand kind", Wrap("""{ "type": "condition", "field": "age", "operator": "lt", "operand": { "kind": "sql", "value": "1=1" } }""") },
        { "unknown property", Wrap("""{ "type": "condition", "field": "age", "operator": "lt", "sql": "1=1" }""") },
        { "missing field", Wrap("""{ "type": "condition", "operator": "lt" }""") },
        { "null field", Wrap("""{ "type": "condition", "field": null, "operator": "isEmpty" }""") },
        { "unknown logic", """{ "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "xor", "children": [] } }""" },
        { "missing filter", """{ "schemaVersion": 1, "rootEntity": "employee" }""" },
        { "null children", """{ "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": null } }""" },
        { "filter is a condition, not a group", """{ "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "condition", "field": "age", "operator": "isEmpty" } }""" },
        { "unknown quantifier", Wrap("""{ "type": "related", "relation": "employees", "quantifier": "most" }""") },
    };

    [Theory]
    [MemberData(nameof(InvalidJson))]
    public void Rejects_invalid_definitions_with_a_Hebrew_message(string scenario, string json)
    {
        var ex = Should.Throw<DefinitionFormatException>(() => DefinitionSerializer.Deserialize(json), scenario);

        ex.Message.ShouldNotBeNullOrWhiteSpace();
        ex.Message.ShouldContain("הגדרת הבקרה");
        ex.TechnicalDetail.ShouldNotBeNullOrWhiteSpace();
    }

    private static string Wrap(string node) =>
        $$"""{ "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": [ {{node}} ] } }""";
}

using System.Text.Json;
using DataControls.Core.Definitions;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Shouldly;
using static DataControls.Core.Validation.ValidationErrorCode;

namespace DataControls.Core.Tests.Validation;

public class DefinitionValidatorTests
{
    private static readonly DefinitionValidator Validator = new();

    // ---------- helpers ----------

    /// <summary>Builds a definition from the JSON of the root group's children.</summary>
    private static ControlDefinition Def(string childrenJson, string root = "employee", int version = 1) =>
        DefinitionSerializer.Deserialize(
            $$"""{ "schemaVersion": {{version}}, "rootEntity": "{{root}}", "filter": { "type": "group", "logic": "and", "children": [ {{childrenJson}} ] } }""");

    private static string Cond(string field, string op, string? operandJson = null) =>
        operandJson is null
            ? $$"""{ "type": "condition", "field": "{{field}}", "operator": "{{op}}" }"""
            : $$"""{ "type": "condition", "field": "{{field}}", "operator": "{{op}}", "operand": {{operandJson}} }""";

    private static string Lit(string valueJson) => $$"""{ "kind": "literal", "value": {{valueJson}} }""";

    private static string FieldRef(string field) => $$"""{ "kind": "field", "field": "{{field}}" }""";

    private static ValidationResult Validate(ControlDefinition definition) => Validator.Validate(definition, TestCatalog.Instance);

    private static ValidationError SingleError(ControlDefinition definition, ValidationErrorCode code, string path)
    {
        var result = Validate(definition);
        result.IsValid.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Path.ShouldBe(path);
        error.Message.ShouldNotBeNullOrWhiteSpace();
        error.Message.Any(c => c is >= 'א' and <= 'ת').ShouldBeTrue("message must be Hebrew");
        return error;
    }

    private const string First = "filter.children[0]";

    // ---------- valid definitions ----------

    [Theory]
    [MemberData(nameof(SampleDefinitions.All), MemberType = typeof(SampleDefinitions))]
    public void All_sample_controls_are_valid(string name, string json)
    {
        var result = Validate(DefinitionSerializer.Deserialize(json));

        result.IsValid.ShouldBeTrue($"{name}: {string.Join(" | ", result.Errors.Select(e => $"{e.Path}: {e.Message}"))}");
    }

    [Fact]
    public void Accepts_every_operand_shape_and_type()
    {
        var definition = Def(string.Join(',',
            Cond("fullName", "contains", Lit("\"כהן\"")),
            Cond("employeeNumber", "in", Lit("[\"E10001\", \"E10002\"]")),
            Cond("salary", "between", Lit("[5000, 9000.5]")),
            Cond("hireDate", "between", Lit("[\"2020-01-01\", \"2020-12-31\"]")),
            Cond("age", "gte", Lit("18")),
            Cond("isActive", "eq", Lit("true")),
            Cond("terminationDate", "isNotEmpty"),
            Cond("age", "lt", FieldRef("salary")),               // Integer vs Decimal: both numeric
            Cond("hireDate", "gt", FieldRef("manager.hireDate"))));

        Validate(definition).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Accepts_enum_values_and_related_conditions_with_or_without_where()
    {
        Validate(Def(Cond("status", "in", Lit("[\"Active\", \"Planned\"]")), root: "project")).IsValid.ShouldBeTrue();
        Validate(Def("""{ "type": "related", "relation": "projects", "quantifier": "any" }""", root: "department")).IsValid.ShouldBeTrue();
        Validate(Def("""{ "type": "related", "relation": "projects", "quantifier": "none", "where": { "type": "group", "logic": "and", "children": [] } }""", root: "department"))
            .IsValid.ShouldBeTrue();
    }

    // ---------- definition structure ----------

    [Fact]
    public void Rejects_unsupported_schema_version() =>
        SingleError(Def(Cond("age", "lt", Lit("18")), version: 2), UnsupportedSchemaVersion, "schemaVersion");

    [Fact]
    public void Rejects_unknown_root_entity_and_stops() =>
        SingleError(Def(Cond("nosuch", "lt", Lit("18")), root: "customer"), UnknownEntity, "rootEntity");

    [Fact]
    public void Rejects_a_control_without_conditions() =>
        SingleError(Def(""), EmptyFilter, "filter");

    [Fact]
    public void Rejects_an_empty_nested_group() =>
        SingleError(Def("""{ "type": "group", "logic": "or", "children": [] }"""), EmptyGroup, First);

    [Fact]
    public void Rejects_a_missing_node_in_a_definition_built_in_code()
    {
        var definition = new ControlDefinition(1, "employee", new GroupNode(LogicalOperator.And, [null!]));

        SingleError(definition, MissingNode, First);
    }

    [Fact]
    public void Rejects_nesting_deeper_than_the_limit()
    {
        var level4 = $$"""{ "type": "group", "logic": "and", "children": [ {{Cond("age", "lt", Lit("18"))}} ] }""";
        var level3 = $$"""{ "type": "group", "logic": "or", "children": [ {{level4}} ] }""";
        var level2 = $$"""{ "type": "group", "logic": "and", "children": [ {{level3}} ] }""";

        SingleError(Def(level2), TooDeep, "filter.children[0].children[0].children[0]");
    }

    [Fact]
    public void Rejects_more_conditions_than_the_limit()
    {
        var many = string.Join(',', Enumerable.Repeat(Cond("age", "gt", Lit("18")), 21));

        SingleError(Def(many), TooManyConditions, "filter");
        Validate(Def(string.Join(',', Enumerable.Repeat(Cond("age", "gt", Lit("18")), 20)))).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Limits_are_configurable()
    {
        var strict = new DefinitionValidator(new ValidationLimits { MaxConditions = 1 });
        var two = Def(Cond("age", "gt", Lit("18")) + "," + Cond("salary", "gt", Lit("1")));

        strict.Validate(two, TestCatalog.Instance).Errors.ShouldHaveSingleItem().Code.ShouldBe(TooManyConditions);
    }

    // ---------- not filled in yet vs. a mistake ----------

    [Theory]
    [InlineData(ValidationErrorCode.EmptyPath, true)]
    [InlineData(ValidationErrorCode.OperandMissing, true)]
    [InlineData(ValidationErrorCode.EmptyList, true)]
    [InlineData(ValidationErrorCode.EmptyGroup, true)]
    [InlineData(ValidationErrorCode.MissingNode, true)]
    [InlineData(ValidationErrorCode.InvalidValue, false)]
    [InlineData(ValidationErrorCode.InvalidRange, false)]
    [InlineData(ValidationErrorCode.UnknownField, false)]
    [InlineData(ValidationErrorCode.OperatorNotAllowedForType, false)]
    public void Tells_what_is_only_not_filled_in_yet_from_a_mistake(ValidationErrorCode code, bool incomplete) =>
        new ValidationError("filter", code, "").IsIncomplete.ShouldBe(incomplete);

    // ---------- field paths (mapped from the resolver) ----------

    [Theory]
    [InlineData("", EmptyPath)]
    [InlineData("salary]; DROP TABLE biz.Employees;--", InvalidPath)]
    [InlineData("car.color", UnknownRelation)]
    [InlineData("manager.manager.manager.salary", TooManyRelations)]
    [InlineData("manager.department.name", TooManyRelations)]   // the default: one relation
    [InlineData("department", RelationWithoutField)]
    [InlineData("shoeSize", UnknownField)]
    public void Reports_field_path_problems_on_the_field(string field, ValidationErrorCode code) =>
        SingleError(Def(Cond(field, "isEmpty")), code, $"{First}.field");

    [Fact]
    public void Rejects_one_to_many_in_a_plain_path() =>
        SingleError(Def(Cond("employees.salary", "isEmpty"), root: "department"), OneToManyInPath, $"{First}.field");

    // ---------- operators and operands ----------

    [Fact]
    public void Rejects_an_operator_that_does_not_fit_the_type()
    {
        var error = SingleError(Def(Cond("age", "contains", Lit("1"))), OperatorNotAllowedForType, $"{First}.operator");

        error.Message.ShouldContain("גיל");
        error.Message.ShouldContain("מספר שלם");
    }

    [Fact]
    public void Requires_an_operand_when_the_operator_needs_one() =>
        SingleError(Def(Cond("age", "lt")), OperandMissing, $"{First}.operand");

    [Fact]
    public void Rejects_an_operand_for_is_empty() =>
        SingleError(Def(Cond("email", "isEmpty", Lit("\"x\""))), OperandNotExpected, $"{First}.operand");

    [Fact]
    public void Rejects_field_operand_for_range_and_list_operators() =>
        SingleError(Def(Cond("salary", "between", FieldRef("manager.salary"))), FieldOperandNotAllowed, $"{First}.operand");

    [Fact]
    public void Rejects_comparing_fields_of_incompatible_types()
    {
        var error = SingleError(Def(Cond("salary", "gt", FieldRef("hireDate"))), IncompatibleFieldTypes, $"{First}.operand");

        error.Message.ShouldContain("שכר חודשי");
        error.Message.ShouldContain("תאריך תחילת העסקה");
    }

    [Fact]
    public void Rejects_comparing_a_field_to_itself() =>
        SingleError(Def(Cond("salary", "gt", FieldRef("salary"))), SameField, $"{First}.operand");

    [Fact]
    public void Reports_problems_in_the_compared_field_path() =>
        SingleError(Def(Cond("hireDate", "gt", FieldRef("manager.nosuch"))), UnknownField, $"{First}.operand.field");

    [Theory]
    [InlineData("age", "lt", "\"18\"")]               // number as string
    [InlineData("age", "lt", "18.5")]                 // not an integer
    [InlineData("salary", "gt", "true")]
    [InlineData("hireDate", "gt", "\"15/03/2020\"")]  // not ISO
    [InlineData("hireDate", "gt", "20200315")]        // number for a date
    [InlineData("isActive", "eq", "\"yes\"")]
    [InlineData("fullName", "contains", "\"   \"")]   // blank text
    [InlineData("fullName", "eq", "null")]
    public void Rejects_literal_values_of_the_wrong_type(string field, string op, string valueJson) =>
        SingleError(Def(Cond(field, op, Lit(valueJson))), InvalidValue, $"{First}.operand.value");

    [Theory]
    [InlineData("age", "18.5", "הערך עבור 'גיל' צריך להיות מספר שלם.")]
    [InlineData("age", "1e30", "הערך עבור 'גיל' חורג מהטווח האפשרי.")]
    [InlineData("age", "9300000000000000000", "הערך עבור 'גיל' חורג מהטווח האפשרי.")]   // whole, beyond 64-bit
    [InlineData("salary", "1e30", "הערך עבור 'שכר חודשי' חורג מהטווח האפשרי.")]
    public void A_number_out_of_range_is_told_apart_from_a_wrong_type(string field, string valueJson, string message) =>
        Validate(Def(Cond(field, "gt", Lit(valueJson)))).Errors.ShouldHaveSingleItem().Message.ShouldBe(message);

    [Fact]
    public void Rejects_text_longer_than_the_limit() =>
        SingleError(Def(Cond("fullName", "contains", Lit($"\"{new string('א', ValidationLimits.Default.MaxTextValueLength + 1)}\""))),
            InvalidValue, $"{First}.operand.value").Message.ShouldContain("ארוך מדי");

    [Fact]
    public void Rejects_an_enum_value_that_is_not_allowed_and_lists_the_allowed_labels()
    {
        var error = SingleError(Def(Cond("status", "eq", Lit("\"Done\"")), root: "project"), InvalidValue, $"{First}.operand.value");

        error.Message.ShouldContain("מתוכנן");
        error.Message.ShouldContain("בוטל");
    }

    [Theory]
    [InlineData("[9000, 5000]")]    // reversed
    [InlineData("[5000]")]          // one value
    [InlineData("5000")]            // not an array
    public void Rejects_invalid_ranges(string valueJson)
    {
        var result = Validate(Def(Cond("salary", "between", Lit(valueJson))));

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(InvalidRange);
    }

    [Fact]
    public void Reports_the_exact_item_of_a_range_or_list_that_is_wrong()
    {
        SingleError(Def(Cond("hireDate", "between", Lit("[\"2020-01-01\", \"oops\"]"))), InvalidValue, $"{First}.operand.value[1]");
        SingleError(Def(Cond("age", "in", Lit("[18, \"x\", 20]"))), InvalidValue, $"{First}.operand.value[1]");
    }

    [Fact]
    public void Rejects_an_empty_list_and_a_list_longer_than_the_limit()
    {
        SingleError(Def(Cond("age", "in", Lit("[]"))), EmptyList, $"{First}.operand.value");

        var tooMany = "[" + string.Join(',', Enumerable.Range(1, 101)) + "]";
        SingleError(Def(Cond("age", "in", Lit(tooMany))), ListTooLong, $"{First}.operand.value");
    }

    // ---------- related-records conditions ----------

    [Fact]
    public void Validates_where_conditions_against_the_related_entity()
    {
        // "salary" belongs to employee - valid inside department.employees
        var ok = Def("""
            { "type": "related", "relation": "employees", "quantifier": "any", "where": { "type": "group", "logic": "and", "children": [
              { "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "literal", "value": 30000 } } ] } }
            """, root: "department");
        Validate(ok).IsValid.ShouldBeTrue();

        // "code" belongs to department, not to employee
        var wrong = Def("""
            { "type": "related", "relation": "employees", "quantifier": "any", "where": { "type": "group", "logic": "and", "children": [
              { "type": "condition", "field": "code", "operator": "isEmpty" } ] } }
            """, root: "department");
        SingleError(wrong, UnknownField, "filter.children[0].where.children[0].field");
    }

    [Fact]
    public void Rejects_unknown_and_many_to_one_relations_in_related_conditions()
    {
        SingleError(Def("""{ "type": "related", "relation": "customers", "quantifier": "any" }""", root: "department"),
            UnknownRelation, $"{First}.relation");

        SingleError(Def("""{ "type": "related", "relation": "department", "quantifier": "none" }"""),
            RelationNotOneToMany, $"{First}.relation");
    }

    [Fact]
    public void Rejects_a_related_condition_inside_another()
    {
        var nested = Def("""
            { "type": "related", "relation": "employees", "quantifier": "any", "where": { "type": "group", "logic": "and", "children": [
              { "type": "related", "relation": "directReports", "quantifier": "none" } ] } }
            """, root: "department");

        SingleError(nested, NestedRelatedNotAllowed, "filter.children[0].where.children[0]");
    }

    // ---------- reporting ----------

    [Fact]
    public void Reports_all_problems_at_once_each_with_its_own_path()
    {
        var definition = Def(string.Join(',',
            Cond("age", "lt", Lit("18")),                        // ok
            Cond("shoeSize", "gt", Lit("40")),                   // unknown field
            """{ "type": "group", "logic": "or", "children": [ """ + Cond("salary", "contains", Lit("\"x\"")) + " ] }",
            Cond("hireDate", "gt", Lit("\"yesterday\""))));       // bad date

        var errors = Validate(definition).Errors;

        errors.Select(e => (e.Path, e.Code)).ShouldBe(
        [
            ("filter.children[1].field", UnknownField),
            ("filter.children[2].children[0].operator", OperatorNotAllowedForType),
            ("filter.children[3].operand.value", InvalidValue),
        ]);
    }

    [Fact]
    public void Validator_is_stateless_between_runs()
    {
        var bad = Def(Cond("shoeSize", "isEmpty"));
        var good = Def(Cond("age", "lt", Lit("18")));

        Validate(bad).IsValid.ShouldBeFalse();
        Validate(good).IsValid.ShouldBeTrue();
        Validate(bad).Errors.Count.ShouldBe(1);
    }

    [Fact]
    public void Literal_values_are_converted_to_typed_values()
    {
        var catalog = TestCatalog.Instance;
        catalog.TryGetField("employee", "hireDate", out var hireDate).ShouldBeTrue();
        catalog.TryGetField("employee", "salary", out var salary).ShouldBeTrue();

        LiteralValues.TryConvert(JsonDocument.Parse("\"2020-03-15\"").RootElement, hireDate!, out var date, out _).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2020, 3, 15));

        LiteralValues.TryConvert(JsonDocument.Parse("15000.50").RootElement, salary!, out var amount, out _).ShouldBeTrue();
        amount.ShouldBe(15000.50m);
    }
}

using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Describing;
using DataControls.Core.Planning;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Shouldly;

namespace DataControls.Core.Tests.Describing;

public class DescribingTests
{
    private static readonly MetadataCatalog Catalog = TestCatalog.Instance;

    private static ControlDefinition Def(string json)
    {
        var definition = DefinitionSerializer.Deserialize(json);
        // two relations allowed, so the wording of longer paths is covered too (the default is one)
        new DefinitionValidator(new ValidationLimits { MaxRelationHops = 2 }).Validate(definition, Catalog).IsValid.ShouldBeTrue();
        return definition;
    }

    private static ControlDefinition One(string field, string op, string? operandJson = null, string root = "employee") => Def(
        $$"""
        { "schemaVersion": 1, "rootEntity": "{{root}}", "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "{{field}}", "operator": "{{op}}"{{(operandJson is null ? "" : $", \"operand\": {operandJson}")}} } ] } }
        """);

    private static string Lit(string valueJson) => $$"""{ "kind": "literal", "value": {{valueJson}} }""";

    private static FieldMeta Field(string entity, string key) =>
        Catalog.TryGetField(entity, key, out var field) ? field : throw new KeyNotFoundException(key);

    // ---------- HebrewFormat ----------

    [Fact]
    public void Formats_values_for_business_users()
    {
        HebrewFormat.Value(15000m, Field("employee", "salary")).ShouldBe("15,000 ₪");
        HebrewFormat.Value(22000.50m, Field("employee", "salary")).ShouldBe("22,000.5 ₪");
        HebrewFormat.Value(16, Field("employee", "age")).ShouldBe("16");
        HebrewFormat.Value(1234567L, Field("employee", "age")).ShouldBe("1,234,567");
        HebrewFormat.Value(new DateOnly(2021, 3, 5), Field("employee", "hireDate")).ShouldBe("05/03/2021");
        HebrewFormat.Value(new DateTime(2021, 3, 5), Field("employee", "hireDate")).ShouldBe("05/03/2021");
        HebrewFormat.Value(true, Field("employee", "isActive")).ShouldBe("כן");
        HebrewFormat.Value(false, Field("employee", "isActive")).ShouldBe("לא");
        HebrewFormat.Value("Cancelled", Field("project", "status")).ShouldBe("בוטל");
        HebrewFormat.Value("Unknown", Field("project", "status")).ShouldBe("Unknown");
        HebrewFormat.Value("כהן", Field("employee", "fullName")).ShouldBe("כהן");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_values_read_as_empty(string? value) =>
        HebrewFormat.Value(value, Field("employee", "email")).ShouldBe("ריק");

    [Fact]
    public void Database_null_reads_as_empty() =>
        HebrewFormat.Value(DBNull.Value, Field("employee", "salary")).ShouldBe("ריק");

    // ---------- DefinitionDescriber ----------

    public static TheoryData<string, string> SampleSentences => new()
    {
        { SampleDefinitions.MinorsWithHighSalary, "גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000 ₪" },
        { SampleDefinitions.TerminationBeforeHire, "תאריך סיום העסקה לפני תאריך תחילת העסקה" },
        { SampleDefinitions.ActiveInInactiveDepartment, "עובד פעיל וגם מחלקה אינה פעילה" },
        { SampleDefinitions.ActiveMissingRequiredData, "עובד פעיל וגם (דואר אלקטרוני ריק או טלפון ריק או שם המחלקה ריק)" },
        { SampleDefinitions.EarnsMoreThanManager, "שכר חודשי גדול משכר חודשי של מנהל ישיר" },
        { SampleDefinitions.ProjectEndBeforeStart, "תאריך סיום לפני תאריך התחלה" },
        { SampleDefinitions.ActiveDepartmentWithoutActiveEmployees, "מחלקה פעילה וגם אין אף עובד פעיל" },
    };

    [Theory]
    [MemberData(nameof(SampleSentences))]
    public void Describes_every_sample_control_in_one_Hebrew_sentence(string json, string expected) =>
        DefinitionDescriber.Describe(Def(json), Catalog).ShouldBe(expected);

    [Theory]
    [InlineData("salary", "neq", "5000", "שכר חודשי שונה מ-5,000 ₪")]
    [InlineData("salary", "between", "[5000, 9000]", "שכר חודשי בין 5,000 ₪ ל-9,000 ₪")]
    [InlineData("hireDate", "gte", "\"2020-01-01\"", "תאריך תחילת העסקה בתאריך או אחרי 01/01/2020")]
    [InlineData("employeeNumber", "in", "[\"E1\", \"E2\"]", "מספר עובד אחד מ: \"E1\", \"E2\"")]
    [InlineData("fullName", "contains", "\"כהן\"", "שם מלא מכיל \"כהן\"")]
    [InlineData("fullName", "eq", "\"רחל\"", "שם מלא שווה ל\"רחל\"")]          // free text quoted, joined to the prefix
    [InlineData("employeeNumber", "neq", "\"E1\"", "מספר עובד שונה מ\"E1\"")]
    [InlineData("isActive", "eq", "false", "עובד אינו פעיל")]
    [InlineData("isActive", "neq", "true", "עובד אינו פעיל")]
    [InlineData("isActive", "neq", "false", "עובד פעיל")]
    [InlineData("manager.department.isActive", "eq", "false", "מחלקה של מנהל ישיר אינה פעילה")]
    public void Describes_each_operator_naturally(string field, string op, string value, string expected) =>
        DefinitionDescriber.Describe(One(field, op, Lit(value)), Catalog).ShouldBe(expected);

    [Fact]
    public void An_empty_check_on_a_yes_no_field_reads_as_a_statement() =>
        DefinitionDescriber.Describe(One("isActive", "isEmpty"), Catalog).ShouldBe("עובד פעיל: ריק");

    [Fact]
    public void Describes_enum_values_by_label_and_empty_checks()
    {
        DefinitionDescriber.Describe(One("status", "in", Lit("[\"Active\", \"Planned\"]"), root: "project"), Catalog)
            .ShouldBe("סטטוס אחד מ: פעיל, מתוכנן");
        DefinitionDescriber.Describe(One("status", "eq", Lit("\"Active\""), root: "project"), Catalog).ShouldBe("סטטוס שווה לפעיל");
        DefinitionDescriber.Describe(One("terminationDate", "isNotEmpty"), Catalog).ShouldBe("תאריך סיום העסקה לא ריק");
    }

    [Fact]
    public void Describes_nested_or_groups_and_related_conditions_without_where()
    {
        var definition = Def("""
            { "schemaVersion": 1, "rootEntity": "department", "filter": { "type": "group", "logic": "or", "children": [
              { "type": "related", "relation": "projects", "quantifier": "none" },
              { "type": "group", "logic": "and", "children": [
                { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": false } },
                { "type": "related", "relation": "employees", "quantifier": "any" } ] } ] } }
            """);

        DefinitionDescriber.Describe(definition, Catalog).ShouldBe("אין פרויקטים או (מחלקה אינה פעילה וגם יש עובדים)");
    }

    [Fact]
    public void Describes_several_conditions_on_related_records()
    {
        var definition = Def("""
            { "schemaVersion": 1, "rootEntity": "department", "filter": { "type": "group", "logic": "and", "children": [
              { "type": "related", "relation": "employees", "quantifier": "any", "where": { "type": "group", "logic": "and", "children": [
                { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } },
                { "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "literal", "value": 30000 } } ] } } ] } }
            """);

        DefinitionDescriber.Describe(definition, Catalog).ShouldBe("יש עובד פעיל עם שכר חודשי גדול מ-30,000 ₪");
    }

    [Theory]
    [InlineData("any", "and", false, "יש עובד שאינו פעיל")]
    [InlineData("none", "and", true, "אין אף עובד פעיל")]
    [InlineData("any", "or", true, "יש עובדים שמתקיים בהם: עובד פעיל")]   // "or": not a description of one record
    public void A_related_records_own_yes_no_field_describes_the_record(string quantifier, string logic, bool active, string expected)
    {
        var definition = DefinitionSerializer.Deserialize($$"""
            { "schemaVersion": 1, "rootEntity": "department", "filter": { "type": "group", "logic": "and", "children": [
              { "type": "related", "relation": "employees", "quantifier": "{{quantifier}}", "where": { "type": "group", "logic": "{{logic}}", "children": [
                { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": {{(active ? "true" : "false")}} } } ] } } ] } }
            """);

        DefinitionDescriber.Describe(definition, Catalog).ShouldBe(expected);
    }

    [Fact]
    public void Wording_follows_the_catalog_gender_of_the_related_entity()
    {
        var feminine = new MetadataCatalog(Catalog.Entities.Select(e => e.Key == "employee" ? e with { Gender = GrammaticalGender.Feminine } : e));
        string Related(string logic, string where) => DefinitionDescriber.Describe(DefinitionSerializer.Deserialize($$"""
            { "schemaVersion": 1, "rootEntity": "department", "filter": { "type": "group", "logic": "and", "children": [
              { "type": "related", "relation": "employees", "quantifier": "any", "where": { "type": "group", "logic": "{{logic}}", "children": [ {{where}} ] } } ] } }
            """), feminine);
        const string HighSalary = """{ "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "literal", "value": 30000 } }""";
        const string Active = """{ "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } }""";

        Related("or", $"{Active}, {HighSalary}").ShouldBe("יש עובדים שמתקיים בהן: עובד פעיל או שכר חודשי גדול מ-30,000 ₪");
        Related("and", HighSalary).ShouldBe("יש עובדים עם שכר חודשי גדול מ-30,000 ₪");
    }

    [Fact]
    public void A_yes_no_field_without_catalog_wording_falls_back_to_yes_no()
    {
        var plain = new MetadataCatalog(Catalog.Entities.Select(e => e with
        {
            Fields = [.. e.Fields.Select(f => f with { TrueText = null, FalseText = null })],
        }));
        var definition = DefinitionSerializer.Deserialize(SampleDefinitions.ActiveInInactiveDepartment);

        DefinitionDescriber.Describe(definition, plain).ShouldBe("עובד פעיל: כן וגם מחלקה פעילה: לא");
    }

    // ---------- ViolationReasonBuilder ----------

    private static IReadOnlyList<string> Reasons(string json, Dictionary<string, object?> row) =>
        ViolationReasonBuilder.Build(QueryPlanner.Plan(Def(json), Catalog, ValidationLimits.Default), row, Catalog);

    [Fact]
    public void Reasons_show_the_actual_value_of_each_met_condition() =>
        Reasons(SampleDefinitions.MinorsWithHighSalary, new() { ["__c0"] = 1, ["__c1"] = 1, ["age"] = 16, ["salary"] = 22000m })
            .ShouldBe(["גיל (16) קטן מ-18", "שכר חודשי (22,000 ₪) גדול מ-15,000 ₪"]);

    [Fact]
    public void Reasons_list_only_the_or_branch_that_fired() =>
        Reasons(SampleDefinitions.ActiveMissingRequiredData,
                new() { ["__c0"] = 1, ["__c1"] = 0, ["__c2"] = 1, ["__c3"] = 0, ["isActive"] = true, ["email"] = "a@b.c", ["phone"] = "   " })
            .ShouldBe(["עובד פעיל", "טלפון ריק"]);

    [Fact]
    public void Reasons_compare_two_actual_values_for_field_to_field_conditions()
    {
        Reasons(SampleDefinitions.EarnsMoreThanManager, new() { ["__c0"] = 1, ["salary"] = 13000m, ["manager__salary"] = 12000m })
            .ShouldBe(["שכר חודשי (13,000 ₪) גדול משכר חודשי של מנהל ישיר (12,000 ₪)"]);

        Reasons(SampleDefinitions.TerminationBeforeHire,
                new() { ["__c0"] = 1, ["terminationDate"] = new DateTime(2020, 1, 1), ["hireDate"] = new DateTime(2021, 3, 5) })
            .ShouldBe(["תאריך סיום העסקה (01/01/2020) לפני תאריך תחילת העסקה (05/03/2021)"]);
    }

    [Fact]
    public void Reasons_for_related_conditions_state_the_fact() =>
        Reasons(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees, new() { ["__c0"] = 1, ["__c1"] = 1, ["isActive"] = true })
            .ShouldBe(["מחלקה פעילה", "אין אף עובד פעיל"]);

    [Fact]
    public void Reasons_show_empty_actual_values()
    {
        var json = One("salary", "neq", Lit("5000"));

        ViolationReasonBuilder.Build(QueryPlanner.Plan(json, Catalog, ValidationLimits.Default), new Dictionary<string, object?> { ["__c0"] = 1, ["salary"] = null }, Catalog)
            .ShouldBe(["שכר חודשי (ריק) שונה מ-5,000 ₪"]);
    }

    [Fact]
    public void Unset_or_missing_flags_produce_no_reason() =>
        Reasons(SampleDefinitions.MinorsWithHighSalary, new() { ["__c0"] = 0, ["age"] = 30, ["salary"] = 1m }).ShouldBeEmpty();
}

namespace DataControls.Core.Tests.Fixtures;

/// <summary>
/// The 7 sample controls (PLAN §4.1) as definition JSON - reused by serializer, validator, planner, renderer
/// and describer tests. Expected violation counts are asserted against the real DB in task 3.6.
/// </summary>
public static class SampleDefinitions
{
    /// <summary>#1 employees under 18 earning more than 15,000 (expected 3).</summary>
    public const string MinorsWithHighSalary = """
        { "schemaVersion": 1, "rootEntity": "employee",
          "filter": { "type": "group", "logic": "and", "children": [
            { "type": "condition", "field": "age", "operator": "lt", "operand": { "kind": "literal", "value": 18 } },
            { "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "literal", "value": 15000 } } ] } }
        """;

    /// <summary>#2 termination date earlier than hire date - field-to-field (expected 4).</summary>
    public const string TerminationBeforeHire = """
        { "schemaVersion": 1, "rootEntity": "employee",
          "filter": { "type": "group", "logic": "and", "children": [
            { "type": "condition", "field": "terminationDate", "operator": "lt", "operand": { "kind": "field", "field": "hireDate" } } ] } }
        """;

    /// <summary>#3 active employees in an inactive department - relation (expected 5).</summary>
    public const string ActiveInInactiveDepartment = """
        { "schemaVersion": 1, "rootEntity": "employee",
          "filter": { "type": "group", "logic": "and", "children": [
            { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } },
            { "type": "condition", "field": "department.isActive", "operator": "eq", "operand": { "kind": "literal", "value": false } } ] } }
        """;

    /// <summary>#4 active employees missing business-required data - OR group nested in AND (expected 6).</summary>
    public const string ActiveMissingRequiredData = """
        { "schemaVersion": 1, "rootEntity": "employee",
          "filter": { "type": "group", "logic": "and", "children": [
            { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } },
            { "type": "group", "logic": "or", "children": [
              { "type": "condition", "field": "email", "operator": "isEmpty" },
              { "type": "condition", "field": "phone", "operator": "isEmpty" },
              { "type": "condition", "field": "department.name", "operator": "isEmpty" } ] } ] } }
        """;

    /// <summary>#5 employee earns more than their own manager - self-relation + field-to-field across a join (expected 3).</summary>
    public const string EarnsMoreThanManager = """
        { "schemaVersion": 1, "rootEntity": "employee",
          "filter": { "type": "group", "logic": "and", "children": [
            { "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "field", "field": "manager.salary" } } ] } }
        """;

    /// <summary>#6 project end date earlier than start date - another root entity (expected 2).</summary>
    public const string ProjectEndBeforeStart = """
        { "schemaVersion": 1, "rootEntity": "project",
          "filter": { "type": "group", "logic": "and", "children": [
            { "type": "condition", "field": "endDate", "operator": "lt", "operand": { "kind": "field", "field": "startDate" } } ] } }
        """;

    /// <summary>#7 active department with no active employees - one-to-many / NOT EXISTS (expected 1).</summary>
    public const string ActiveDepartmentWithoutActiveEmployees = """
        { "schemaVersion": 1, "rootEntity": "department",
          "filter": { "type": "group", "logic": "and", "children": [
            { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } },
            { "type": "related", "relation": "employees", "quantifier": "none",
              "where": { "type": "group", "logic": "and", "children": [
                { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } } ] } } ] } }
        """;

    public static TheoryData<string, string> All => new()
    {
        { nameof(MinorsWithHighSalary), MinorsWithHighSalary },
        { nameof(TerminationBeforeHire), TerminationBeforeHire },
        { nameof(ActiveInInactiveDepartment), ActiveInInactiveDepartment },
        { nameof(ActiveMissingRequiredData), ActiveMissingRequiredData },
        { nameof(EarnsMoreThanManager), EarnsMoreThanManager },
        { nameof(ProjectEndBeforeStart), ProjectEndBeforeStart },
        { nameof(ActiveDepartmentWithoutActiveEmployees), ActiveDepartmentWithoutActiveEmployees },
    };
}

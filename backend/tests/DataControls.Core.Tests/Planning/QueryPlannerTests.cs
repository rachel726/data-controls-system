using DataControls.Core.Definitions;
using DataControls.Core.Planning;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Shouldly;

namespace DataControls.Core.Tests.Planning;

public class QueryPlannerTests
{
    /// <summary>Two relations allowed, so chains of joins are planned too (the default is one).</summary>
    private static readonly ValidationLimits Limits = new() { MaxRelationHops = 2 };

    private static QueryModel Plan(string json)
    {
        var definition = DefinitionSerializer.Deserialize(json);
        new DefinitionValidator(Limits).Validate(definition, TestCatalog.Instance).IsValid.ShouldBeTrue("test definitions must be valid");
        return QueryPlanner.Plan(definition, TestCatalog.Instance, Limits);
    }

    private static QueryModel PlanChildren(string childrenJson, string root = "employee") => Plan(
        $$"""{ "schemaVersion": 1, "rootEntity": "{{root}}", "filter": { "type": "group", "logic": "and", "children": [ {{childrenJson}} ] } }""");

    private static string Cond(string field, string op, string operand) =>
        $$"""{ "type": "condition", "field": "{{field}}", "operator": "{{op}}", "operand": {{operand}} }""";

    private static string IsEmpty(string field) => $$"""{ "type": "condition", "field": "{{field}}", "operator": "isEmpty" }""";

    // ---------- root and simple conditions ----------

    [Fact]
    public void Plans_conditions_on_the_root_table_without_joins()
    {
        var model = Plan(SampleDefinitions.MinorsWithHighSalary);

        model.Root.ShouldBe(new TableSource("biz", "Employees", "r", "Id"));
        model.Joins.ShouldBeEmpty();
        model.Filter.Logic.ShouldBe(LogicalOperator.And);

        var age = model.Filter.Children[0].ShouldBeOfType<ComparisonPredicate>();
        age.Left.ShouldBe(new FieldRef("r", age.Left.Field));
        age.Left.Field.Key.ShouldBe("age");
        age.Operator.ShouldBe(ConditionOperator.Lt);
        age.Right.ShouldBe(new ValueOperand(18L));

        model.Filter.Children[1].ShouldBeOfType<ComparisonPredicate>().Right.ShouldBe(new ValueOperand(15000m));
    }

    [Fact]
    public void Assigns_one_flag_per_root_condition_in_order()
    {
        var model = Plan(SampleDefinitions.ActiveMissingRequiredData);   // isActive AND (email OR phone OR department.name)

        model.Flags.Select(f => (f.Index, f.NodePath)).ShouldBe(
        [
            (0, "filter.children[0]"),
            (1, "filter.children[1].children[0]"),
            (2, "filter.children[1].children[1]"),
            (3, "filter.children[1].children[2]"),
        ]);

        var or = model.Filter.Children[1].ShouldBeOfType<GroupPredicate>();
        or.Logic.ShouldBe(LogicalOperator.Or);
        or.Children.Cast<ComparisonPredicate>().Select(c => c.Flag).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Converts_literals_to_typed_values()
    {
        var model = PlanChildren(string.Join(',',
            Cond("hireDate", "between", """{ "kind": "literal", "value": ["2020-01-01", "2020-12-31"] }"""),
            Cond("employeeNumber", "in", """{ "kind": "literal", "value": ["E1", "E2"] }"""),
            Cond("isActive", "eq", """{ "kind": "literal", "value": true }""")));

        model.Filter.Children[0].ShouldBeOfType<ComparisonPredicate>().Right
            .ShouldBe(new RangeOperand(new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31)));
        model.Filter.Children[1].ShouldBeOfType<ComparisonPredicate>().Right
            .ShouldBeOfType<ListOperand>().Values.ShouldBe(["E1", "E2"]);
        model.Filter.Children[2].ShouldBeOfType<ComparisonPredicate>().Right.ShouldBe(new ValueOperand(true));
    }

    // ---------- joins ----------

    [Fact]
    public void Joins_a_related_table_through_a_many_to_one_relation()
    {
        var model = Plan(SampleDefinitions.ActiveInInactiveDepartment);

        var join = model.Joins.ShouldHaveSingleItem();
        join.ShouldBe(new Join(new TableSource("biz", "Departments", "j1", "Id"), "Id", "r", "DepartmentId", "department"));

        model.Filter.Children[1].ShouldBeOfType<ComparisonPredicate>().Left.Alias.ShouldBe("j1");
    }

    [Fact]
    public void Reuses_one_join_for_several_conditions_on_the_same_relation()
    {
        var model = PlanChildren(string.Join(',',
            Cond("department.isActive", "eq", """{ "kind": "literal", "value": false }"""),
            IsEmpty("department.name"),
            IsEmpty("department.code")));

        model.Joins.ShouldHaveSingleItem().Table.Alias.ShouldBe("j1");
        model.Filter.Children.Cast<ComparisonPredicate>().Select(c => c.Left.Alias).ShouldBe(["j1", "j1", "j1"]);
    }

    [Fact]
    public void Keeps_different_relation_chains_to_the_same_table_apart()
    {
        // my department vs. my manager's department: same table, different rows → two joins
        var model = PlanChildren(string.Join(',',
            IsEmpty("department.name"),
            IsEmpty("manager.department.name")));

        model.Joins.Select(j => (j.Table.Alias, j.Table.Table, j.ParentAlias, j.ParentColumn, j.RelationPathKey)).ShouldBe(
        [
            ("j1", "Departments", "r", "DepartmentId", "department"),
            ("j2", "Employees", "r", "ManagerId", "manager"),
            ("j3", "Departments", "j2", "DepartmentId", "manager.department"),
        ]);

        model.Filter.Children.Cast<ComparisonPredicate>().Select(c => c.Left.Alias).ShouldBe(["j1", "j3"]);
    }

    [Fact]
    public void Plans_a_field_to_field_comparison_across_a_self_join()
    {
        var model = Plan(SampleDefinitions.EarnsMoreThanManager);

        model.Joins.ShouldHaveSingleItem().ShouldBe(new Join(new TableSource("biz", "Employees", "j1", "Id"), "Id", "r", "ManagerId", "manager"));

        var comparison = model.Filter.Children.Single().ShouldBeOfType<ComparisonPredicate>();
        comparison.Left.Alias.ShouldBe("r");
        comparison.Right.ShouldBeOfType<FieldOperandRef>().Field.Alias.ShouldBe("j1");

        var flag = model.Flags.Single();
        flag.ColumnKey.ShouldBe("salary");
        flag.CompareColumnKey.ShouldBe("manager__salary");
        flag.CompareField!.DisplayName.ShouldBe("שכר חודשי של מנהל ישיר");
    }

    // ---------- related records (EXISTS) ----------

    [Fact]
    public void Plans_a_related_records_condition_as_a_correlated_not_exists()
    {
        var model = Plan(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees);

        model.Root.Table.ShouldBe("Departments");
        model.Joins.ShouldBeEmpty();

        var exists = model.Filter.Children[1].ShouldBeOfType<ExistsPredicate>();
        exists.Negated.ShouldBeTrue();
        exists.Table.ShouldBe(new TableSource("biz", "Employees", "x1", "Id"));
        (exists.Column, exists.ParentAlias, exists.ParentColumn).ShouldBe(("DepartmentId", "r", "Id"));

        var inner = exists.Where.ShouldNotBeNull().Children.Single().ShouldBeOfType<ComparisonPredicate>();
        inner.Left.Alias.ShouldBe("x1");
        inner.Left.Field.Key.ShouldBe("isActive");
        inner.Flag.ShouldBeNull();   // per related row - not a reason of the department

        var flag = model.Flags[1];
        flag.Node.ShouldBeOfType<RelatedNode>();
        flag.Relation!.Key.ShouldBe("employees");
        flag.RelatedEntity!.Key.ShouldBe("employee");
        model.Flags.Count.ShouldBe(2);
    }

    [Fact]
    public void A_subquery_has_its_own_joins_and_aliases()
    {
        var model = PlanChildren("""
            { "type": "related", "relation": "employees", "quantifier": "any", "where": { "type": "group", "logic": "and", "children": [
              { "type": "condition", "field": "manager.salary", "operator": "lt", "operand": { "kind": "literal", "value": 10000 } } ] } }
            """, root: "department");

        model.Joins.ShouldBeEmpty();   // nothing joined in the outer query

        var exists = model.Filter.Children.Single().ShouldBeOfType<ExistsPredicate>();
        exists.Negated.ShouldBeFalse();
        exists.Joins.ShouldHaveSingleItem().ShouldBe(
            new Join(new TableSource("biz", "Employees", "x1_j1", "Id"), "Id", "x1", "ManagerId", "manager"));
        exists.Where!.Children.Single().ShouldBeOfType<ComparisonPredicate>().Left.Alias.ShouldBe("x1_j1");
    }

    [Fact]
    public void A_related_condition_without_where_has_no_inner_predicate()
    {
        var model = PlanChildren("""{ "type": "related", "relation": "projects", "quantifier": "any" }""", root: "department");

        model.Filter.Children.Single().ShouldBeOfType<ExistsPredicate>().Where.ShouldBeNull();
    }

    // ---------- result columns ----------

    [Fact]
    public void Result_columns_are_visible_fields_then_condition_fields()
    {
        var model = Plan(SampleDefinitions.MinorsWithHighSalary);

        model.Columns.Select(c => (c.Key, c.IsConditionField)).ShouldBe(
        [
            ("employeeNumber", false),
            ("fullName", false),
            ("isActive", false),
            ("age", true),
            ("salary", true),
        ]);
        model.Title.Field.Key.ShouldBe("fullName");
    }

    [Fact]
    public void A_visible_field_used_in_a_condition_is_not_duplicated_and_related_fields_get_flat_keys()
    {
        var model = Plan(SampleDefinitions.ActiveInInactiveDepartment);   // isActive (visible) + department.isActive

        model.Columns.Select(c => (c.Key, c.IsConditionField)).ShouldBe(
        [
            ("employeeNumber", false),
            ("fullName", false),
            ("isActive", true),
            ("department__isActive", true),
        ]);
        model.Columns[^1].Expression.Alias.ShouldBe("j1");
    }

    [Fact]
    public void Fields_inside_a_related_condition_are_not_result_columns()
    {
        var model = Plan(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees);

        model.Columns.Select(c => c.Key).ShouldBe(["name", "code", "isActive"]);
    }

    // ---------- guarantees ----------

    [Theory]
    [MemberData(nameof(SampleDefinitions.All), MemberType = typeof(SampleDefinitions))]
    public void Planning_is_deterministic(string name, string json)
    {
        var first = Plan(json);
        var second = Plan(json);

        second.Joins.ShouldBe(first.Joins, name);
        second.Columns.Select(c => c.Key).ShouldBe(first.Columns.Select(c => c.Key));
        second.Flags.Select(f => f.NodePath).ShouldBe(first.Flags.Select(f => f.NodePath));
    }

    [Fact]
    public void Refuses_a_definition_that_was_not_validated()
    {
        var invalid = new ControlDefinition(1, "employee",
            new GroupNode(LogicalOperator.And, [new ConditionNode("shoeSize", ConditionOperator.IsEmpty)]));

        Should.Throw<PlanningException>(() => QueryPlanner.Plan(invalid, TestCatalog.Instance, ValidationLimits.Default)).Message.ShouldContain("validate");
    }
}

using DataControls.Core.Catalog;
using DataControls.Core.Paths;
using DataControls.Core.Tests.Fixtures;
using Shouldly;

namespace DataControls.Core.Tests.Paths;

public class FieldPathResolverTests
{
    private static readonly MetadataCatalog Catalog = TestCatalog.Instance;

    private static ResolvedField Resolve(string root, string path)
    {
        FieldPathResolver.TryResolve(Catalog, root, path, out var resolved, out var error)
            .ShouldBeTrue(error?.Message);
        return resolved;
    }

    private static PathError Fail(string root, string? path, int maxHops = 2)
    {
        FieldPathResolver.TryResolve(Catalog, root, path, out var resolved, out var error, maxHops)
            .ShouldBeFalse($"'{path}' should not resolve, got {resolved?.Field.Key}");
        error.Message.ShouldNotBeNullOrWhiteSpace();
        return error;
    }

    [Fact]
    public void Resolves_a_field_of_the_root_entity()
    {
        var age = Resolve("employee", "age");

        age.Field.Key.ShouldBe("age");
        age.Entity.Key.ShouldBe("employee");
        age.RelationChain.ShouldBeEmpty();
        age.RelationPathKey.ShouldBe("");
        age.DisplayName.ShouldBe("גיל");
    }

    [Fact]
    public void Resolves_a_field_through_a_many_to_one_relation()
    {
        var active = Resolve("employee", "department.isActive");

        active.Field.DisplayName.ShouldBe("מחלקה פעילה");
        active.Entity.Key.ShouldBe("department");
        active.RelationChain.Select(r => r.Key).ShouldBe(["department"]);
        active.RelationPathKey.ShouldBe("department");
        active.DisplayName.ShouldBe("מחלקה פעילה");   // the name says whose it is
    }

    [Fact]
    public void Resolves_a_self_relation()
    {
        var managerSalary = Resolve("employee", "manager.salary");

        managerSalary.Entity.Key.ShouldBe("employee");
        managerSalary.Field.Key.ShouldBe("salary");
        managerSalary.DisplayName.ShouldBe("שכר חודשי של מנהל ישיר");   // the employee has a "שכר חודשי" too
    }

    [Fact]
    public void Resolves_two_hops()
    {
        var path = Resolve("employee", "manager.department.name");

        path.RelationChain.Select(r => r.Key).ShouldBe(["manager", "department"]);
        path.RelationPathKey.ShouldBe("manager.department");
        path.Entity.Key.ShouldBe("department");
        path.DisplayName.ShouldBe("שם המחלקה של מחלקה של מנהל ישיר");
    }

    [Fact]
    public void Same_relation_key_resolves_per_entity()
    {
        Resolve("project", "department.name").DisplayName.ShouldBe("שם המחלקה");
    }

    [Fact]
    public void Rejects_more_hops_than_allowed()
    {
        Fail("employee", "manager.manager.manager.salary").Code.ShouldBe(PathErrorCode.TooManyRelations);
        Fail("employee", "manager.department.name", maxHops: 1).Code.ShouldBe(PathErrorCode.TooManyRelations);
        // in business words - never the technical path
        Fail("employee", "manager.manager.employeeNumber", maxHops: 1).Message
            .ShouldBe("לא ניתן להשתמש בשדה של מנהל ישיר של מנהל ישיר - אפשר לבחור שדות דרך קשר אחד בלבד, למשל שדות של מנהל ישיר.");
    }

    [Fact]
    public void Rejects_a_one_to_many_relation_in_a_plain_path_and_points_to_related_conditions()
    {
        var error = Fail("department", "employees.salary");

        error.Code.ShouldBe(PathErrorCode.OneToManyInPath);
        error.Message.ShouldContain("תנאי 'יש / אין'");
    }

    [Fact]
    public void Rejects_unknown_field_and_relation_in_business_terms()
    {
        var field = Fail("employee", "shoeSize");
        field.Code.ShouldBe(PathErrorCode.UnknownField);
        field.Message.ShouldContain("לעובד");

        Fail("employee", "car.color").Code.ShouldBe(PathErrorCode.UnknownRelation);
        Fail("employee", "department.budget").Code.ShouldBe(PathErrorCode.UnknownField);
    }

    [Fact]
    public void Rejects_a_relation_without_a_field()
    {
        var error = Fail("employee", "department");

        error.Code.ShouldBe(PathErrorCode.RelationWithoutField);
        error.Message.ShouldContain("מחלקה");
    }

    [Fact]
    public void Rejects_a_field_used_as_a_relation() =>
        Fail("employee", "salary.amount").Code.ShouldBe(PathErrorCode.InvalidFormat);

    [Fact]
    public void Rejects_an_unknown_root_entity() =>
        Fail("customer", "name").Code.ShouldBe(PathErrorCode.UnknownEntity);

    [Theory]
    [InlineData(null, PathErrorCode.EmptyPath)]
    [InlineData("", PathErrorCode.EmptyPath)]
    [InlineData("   ", PathErrorCode.EmptyPath)]
    [InlineData("department..name", PathErrorCode.InvalidFormat)]
    [InlineData(".age", PathErrorCode.InvalidFormat)]
    [InlineData("age.", PathErrorCode.InvalidFormat)]
    [InlineData(" age", PathErrorCode.InvalidFormat)]
    [InlineData("Age", PathErrorCode.InvalidFormat)]
    [InlineData("גיל", PathErrorCode.InvalidFormat)]
    [InlineData("salary]; DROP TABLE biz.Employees;--", PathErrorCode.InvalidFormat)]
    [InlineData("salary' OR '1'='1", PathErrorCode.InvalidFormat)]
    [InlineData("[Salary]", PathErrorCode.InvalidFormat)]
    [InlineData("department.[Name]", PathErrorCode.InvalidFormat)]
    public void Rejects_malformed_and_injection_like_paths_before_touching_the_catalog(string? path, PathErrorCode expected) =>
        Fail("employee", path).Code.ShouldBe(expected);

    // ---------- the fields a control may use (what the UI lists) ----------

    [Fact]
    public void Lists_own_fields_then_those_of_directly_related_records_under_their_names()
    {
        var fields = FieldPathResolver.ReachableFields(TestCatalog.Instance, "employee", maxRelationHops: 1);

        fields.Select(f => f.Owner).Distinct().ShouldBe(["עובד", "מחלקה", "מנהל ישיר"]);   // the root first
        var byPath = fields.ToDictionary(f => f.Path);
        byPath["department.name"].DisplayName.ShouldBe("שם המחלקה");
        byPath["manager.salary"].DisplayName.ShouldBe("שכר חודשי של מנהל ישיר");
        byPath.ShouldNotContainKey("manager.department.name");             // beyond the limit
        fields.ShouldNotContain(f => f.Path.StartsWith("directReports.", StringComparison.Ordinal));   // one-to-many: a "יש / אין" condition
        foreach (var field in fields)
        {
            FieldPathResolver.TryResolve(TestCatalog.Instance, "employee", field.Path, out _, out _, 1).ShouldBeTrue(field.Path);   // validation accepts each
        }
    }

    [Fact]
    public void Follows_the_configured_limit()
    {
        var fields = FieldPathResolver.ReachableFields(TestCatalog.Instance, "employee", maxRelationHops: 2);

        fields.Single(f => f.Path == "manager.department.name").Owner.ShouldBe("מחלקה של מנהל ישיר");
        fields.Select(f => f.Owner).ShouldContain("מנהל ישיר של מנהל ישיר");
    }
}

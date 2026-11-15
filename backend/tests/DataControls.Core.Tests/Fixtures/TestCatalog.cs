using DataControls.Core.Catalog;

namespace DataControls.Core.Tests.Fixtures;

/// <summary>
/// In-memory catalog mirroring database/scripts/04_seed_catalog.sql, so the engine can be tested without a database.
/// Keep in sync with the seed (an integration test in task 3.1 compares the two).
/// </summary>
public static class TestCatalog
{
    public static MetadataCatalog Instance { get; } = Build();

    private static MetadataCatalog Build()
    {
        var employee = new EntityMeta
        {
            Key = "employee", DisplayName = "עובד", DisplayNamePlural = "עובדים",
            Description = "עובדי הארגון - כולל עובדים שסיימו את עבודתם.",
            SchemaName = "biz", TableName = "Employees", PrimaryKeyColumn = "Id", DisplayFieldKey = "fullName", SortOrder = 1,
            Fields =
            [
                Field("employeeNumber", "מספר עובד", "EmployeeNumber", DataType.Text, required: true, visible: true, searchable: true),
                Field("fullName", "שם מלא", "FirstName", DataType.Text, required: true, visible: true, searchable: true) with
                    { Transform = FieldTransform.FullName, TransformArg = "LastName" },
                Field("firstName", "שם פרטי", "FirstName", DataType.Text, required: true),
                Field("lastName", "שם משפחה", "LastName", DataType.Text, required: true),
                Field("isActive", "עובד פעיל", "IsActive", DataType.Boolean, required: true, visible: true) with { TrueText = "פעיל", FalseText = "אינו פעיל" },
                Field("age", "גיל", "BirthDate", DataType.Integer) with { Transform = FieldTransform.YearsSince },
                Field("birthDate", "תאריך לידה", "BirthDate", DataType.Date, required: true),
                Field("email", "דואר אלקטרוני", "Email", DataType.Text, required: true, searchable: true),
                Field("phone", "טלפון", "Phone", DataType.Text, required: true),
                Field("hireDate", "תאריך תחילת העסקה", "HireDate", DataType.Date, required: true),
                Field("terminationDate", "תאריך סיום העסקה", "TerminationDate", DataType.Date),
                Field("salary", "שכר חודשי", "Salary", DataType.Decimal, required: true) with { Unit = "₪" },
            ],
            Relations =
            [
                Relation("department", "employee", "department", "DepartmentId", "Id", Cardinality.ManyToOne, "מחלקה"),
                Relation("manager", "employee", "employee", "ManagerId", "Id", Cardinality.ManyToOne, "מנהל ישיר"),
                Relation("directReports", "employee", "employee", "Id", "ManagerId", Cardinality.OneToMany, "עובדים כפופים"),
            ],
        };

        var department = new EntityMeta
        {
            Key = "department", DisplayName = "מחלקה", DisplayNamePlural = "מחלקות", Gender = GrammaticalGender.Feminine,
            SchemaName = "biz", TableName = "Departments", PrimaryKeyColumn = "Id", DisplayFieldKey = "name", SortOrder = 2,
            Fields =
            [
                Field("name", "שם המחלקה", "Name", DataType.Text, required: true, visible: true, searchable: true),
                Field("code", "קוד מחלקה", "Code", DataType.Text, required: true, visible: true, searchable: true),
                Field("isActive", "מחלקה פעילה", "IsActive", DataType.Boolean, required: true, visible: true) with { TrueText = "פעילה", FalseText = "אינה פעילה" },
            ],
            Relations =
            [
                Relation("employees", "department", "employee", "Id", "DepartmentId", Cardinality.OneToMany, "עובדים"),
                Relation("projects", "department", "project", "Id", "DepartmentId", Cardinality.OneToMany, "פרויקטים"),
            ],
        };

        var project = new EntityMeta
        {
            Key = "project", DisplayName = "פרויקט", DisplayNamePlural = "פרויקטים",
            SchemaName = "biz", TableName = "Projects", PrimaryKeyColumn = "Id", DisplayFieldKey = "name", SortOrder = 3,
            Fields =
            [
                Field("name", "שם הפרויקט", "Name", DataType.Text, required: true, visible: true, searchable: true),
                Field("status", "סטטוס", "Status", DataType.Enum, required: true, visible: true) with
                {
                    AllowedValues =
                    [
                        new("Planned", "מתוכנן"), new("Active", "פעיל"), new("Completed", "הסתיים"), new("Cancelled", "בוטל"),
                    ],
                },
                Field("startDate", "תאריך התחלה", "StartDate", DataType.Date, required: true, visible: true),
                Field("endDate", "תאריך סיום", "EndDate", DataType.Date, visible: true),
                Field("budget", "תקציב", "Budget", DataType.Decimal) with { Unit = "₪" },
            ],
            Relations =
            [
                Relation("department", "project", "department", "DepartmentId", "Id", Cardinality.ManyToOne, "מחלקה אחראית"),
            ],
        };

        return new MetadataCatalog([employee, department, project]);
    }

    private static FieldMeta Field(string key, string displayName, string column, DataType type,
        bool required = false, bool visible = false, bool searchable = false) => new()
    {
        Key = key, DisplayName = displayName, SourceColumn = column, DataType = type,
        IsRequiredByBusiness = required, IsVisibleInResults = visible, IsSearchable = searchable,
    };

    private static RelationMeta Relation(string key, string from, string to, string fromColumn, string toColumn,
        Cardinality cardinality, string displayName) => new()
    {
        Key = key, FromEntityKey = from, ToEntityKey = to, FromColumn = fromColumn, ToColumn = toColumn,
        Cardinality = cardinality, DisplayName = displayName,
    };
}

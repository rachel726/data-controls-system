/* =====================================================================
   04 - The business catalog: what business users see instead of tables
   and columns. This is the ONLY place that maps business terms to the
   physical schema; the engine, API, UI and AI read it at runtime.

   Adding a table/field to the system = adding rows here. No code changes.

   Relation semantics (join condition):  <to>.ToColumn = <from>.FromColumn
     ManyToOne  employee.department : Departments.Id         = Employees.DepartmentId
     OneToMany  department.employees: Employees.DepartmentId = Departments.Id
   Every FK is registered in both directions.

   Re-runnable: entities are upserted by Key (saved controls reference them);
   fields and relations of these entities are replaced.
   ===================================================================== */
USE DataControlsDb;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

/* ---------- entities ---------- */
MERGE meta.Entities AS t
USING (VALUES
    ('employee',   N'עובד',    N'עובדים',   'Masculine', N'עובדי הארגון - כולל עובדים שסיימו את עבודתם.',          'biz', 'Employees',   'Id', 'fullName', 1),
    ('department', N'מחלקה',   N'מחלקות',   'Feminine',  N'היחידות הארגוניות. מחלקה לא פעילה היא מחלקה שנסגרה.',   'biz', 'Departments', 'Id', 'name',     2),
    ('project',    N'פרויקט',  N'פרויקטים', 'Masculine', N'פרויקטים של הארגון, כל אחד באחריות מחלקה אחת.',         'biz', 'Projects',    'Id', 'name',     3)
) AS s ([Key], DisplayName, DisplayNamePlural, Gender, Description, SchemaName, TableName, PrimaryKeyColumn, DisplayFieldKey, SortOrder)
ON t.[Key] = s.[Key]
WHEN MATCHED THEN UPDATE SET
    DisplayName = s.DisplayName, DisplayNamePlural = s.DisplayNamePlural, Gender = s.Gender, Description = s.Description,
    SchemaName = s.SchemaName, TableName = s.TableName, PrimaryKeyColumn = s.PrimaryKeyColumn,
    DisplayFieldKey = s.DisplayFieldKey, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT ([Key], DisplayName, DisplayNamePlural, Gender, Description, SchemaName, TableName, PrimaryKeyColumn, DisplayFieldKey, SortOrder)
    VALUES (s.[Key], s.DisplayName, s.DisplayNamePlural, s.Gender, s.Description, s.SchemaName, s.TableName, s.PrimaryKeyColumn, s.DisplayFieldKey, s.SortOrder);

DECLARE @employee   INT = (SELECT Id FROM meta.Entities WHERE [Key] = 'employee');
DECLARE @department INT = (SELECT Id FROM meta.Entities WHERE [Key] = 'department');
DECLARE @project    INT = (SELECT Id FROM meta.Entities WHERE [Key] = 'project');

DELETE FROM meta.Relations WHERE FromEntityId IN (@employee, @department, @project);
DELETE FROM meta.Fields    WHERE EntityId     IN (@employee, @department, @project);

/* ---------- fields ----------
   columns: entity, key, display name, description, source column, data type, transform, transform arg,
            required by business, visible in results by default, searchable, allowed values, unit, sort */
INSERT meta.Fields (EntityId, [Key], DisplayName, Description, SourceColumn, DataType, Transform, TransformArg,
                    IsRequiredByBusiness, IsVisibleInResults, IsSearchable, AllowedValuesJson, Unit, SortOrder)
VALUES
    -- employee
    (@employee, 'employeeNumber',  N'מספר עובד',          N'מספר העובד הייחודי בארגון.',                       'EmployeeNumber',  'Text',    'None',       NULL,       1, 1, 1, NULL, NULL, 1),
    (@employee, 'fullName',        N'שם מלא',             N'שם פרטי ושם משפחה.',                               'FirstName',       'Text',    'FullName',   'LastName', 1, 1, 1, NULL, NULL, 2),
    (@employee, 'firstName',       N'שם פרטי',            NULL,                                                 'FirstName',       'Text',    'None',       NULL,       1, 0, 0, NULL, NULL, 3),
    (@employee, 'lastName',        N'שם משפחה',           NULL,                                                 'LastName',        'Text',    'None',       NULL,       1, 0, 0, NULL, NULL, 4),
    (@employee, 'isActive',        N'עובד פעיל',          N'האם העובד מועסק כיום בארגון.',                     'IsActive',        'Boolean', 'None',       NULL,       1, 1, 0, NULL, NULL, 5),
    (@employee, 'age',             N'גיל',                N'גיל העובד בשנים מלאות, מחושב מתאריך הלידה.',      'BirthDate',       'Integer', 'YearsSince', NULL,       0, 0, 0, NULL, NULL, 6),
    (@employee, 'birthDate',       N'תאריך לידה',         NULL,                                                 'BirthDate',       'Date',    'None',       NULL,       1, 0, 0, NULL, NULL, 7),
    (@employee, 'email',           N'דואר אלקטרוני',      N'כתובת הדואר האלקטרוני של העובד. שדה חובה לעובד פעיל.', 'Email',      'Text',    'None',       NULL,       1, 0, 1, NULL, NULL, 8),
    (@employee, 'phone',           N'טלפון',              N'מספר הטלפון של העובד. שדה חובה לעובד פעיל.',       'Phone',           'Text',    'None',       NULL,       1, 0, 0, NULL, NULL, 9),
    (@employee, 'hireDate',        N'תאריך תחילת העסקה', NULL,                                                 'HireDate',        'Date',    'None',       NULL,       1, 0, 0, NULL, NULL, 10),
    (@employee, 'terminationDate', N'תאריך סיום העסקה',  N'ריק לעובד שעדיין מועסק.',                            'TerminationDate', 'Date',    'None',       NULL,       0, 0, 0, NULL, NULL, 11),
    (@employee, 'salary',          N'שכר חודשי',          N'שכר ברוטו חודשי.',                                 'Salary',          'Decimal', 'None',       NULL,       1, 0, 0, NULL, N'₪', 12),
    -- department
    (@department, 'name',          N'שם המחלקה',          NULL,                                                 'Name',            'Text',    'None',       NULL,       1, 1, 1, NULL, NULL, 1),
    (@department, 'code',          N'קוד מחלקה',          N'קוד מקוצר של המחלקה.',                             'Code',            'Text',    'None',       NULL,       1, 1, 1, NULL, NULL, 2),
    (@department, 'isActive',      N'מחלקה פעילה',        N'האם המחלקה פעילה. מחלקה לא פעילה היא מחלקה שנסגרה.', 'IsActive',     'Boolean', 'None',       NULL,       1, 1, 0, NULL, NULL, 3),
    -- project
    (@project, 'name',             N'שם הפרויקט',         NULL,                                                 'Name',            'Text',    'None',       NULL,       1, 1, 1, NULL, NULL, 1),
    (@project, 'status',           N'סטטוס',              N'שלב הפרויקט.',                                      'Status',          'Enum',    'None',       NULL,       1, 1, 0,
        N'[{"value":"Planned","label":"מתוכנן"},{"value":"Active","label":"פעיל"},{"value":"Completed","label":"הסתיים"},{"value":"Cancelled","label":"בוטל"}]', NULL, 2),
    (@project, 'startDate',        N'תאריך התחלה',        NULL,                                                 'StartDate',       'Date',    'None',       NULL,       1, 1, 0, NULL, NULL, 3),
    (@project, 'endDate',          N'תאריך סיום',         N'ריק לפרויקט שטרם הסתיים.',                          'EndDate',         'Date',    'None',       NULL,       0, 1, 0, NULL, NULL, 4),
    (@project, 'budget',           N'תקציב',              NULL,                                                 'Budget',          'Decimal', 'None',       NULL,       0, 0, 0, NULL, N'₪', 5);

/* ---------- wording of yes/no fields, agreeing with the entity: "עובד פעיל", "מחלקה אינה פעילה" ---------- */
UPDATE f SET TrueText = w.TrueText, FalseText = w.FalseText
FROM meta.Fields AS f
JOIN (VALUES
    (@employee,   'isActive', N'פעיל',  N'אינו פעיל'),
    (@department, 'isActive', N'פעילה', N'אינה פעילה')
) AS w (EntityId, [Key], TrueText, FalseText) ON w.EntityId = f.EntityId AND w.[Key] = f.[Key];

/* ---------- relations ---------- */
INSERT meta.Relations ([Key], FromEntityId, ToEntityId, FromColumn, ToColumn, Cardinality, DisplayName, Description)
VALUES
    ('department',    @employee,   @department, 'DepartmentId', 'Id',           'ManyToOne', N'מחלקה',          N'המחלקה שבה העובד משובץ.'),
    ('manager',       @employee,   @employee,   'ManagerId',    'Id',           'ManyToOne', N'מנהל ישיר',      N'העובד שאליו העובד כפוף.'),
    ('directReports', @employee,   @employee,   'Id',           'ManagerId',    'OneToMany', N'עובדים כפופים',  N'העובדים שהעובד מנהל ישירות.'),
    ('employees',     @department, @employee,   'Id',           'DepartmentId', 'OneToMany', N'עובדים',         N'העובדים המשובצים במחלקה.'),
    ('projects',      @department, @project,    'Id',           'DepartmentId', 'OneToMany', N'פרויקטים',       N'הפרויקטים שבאחריות המחלקה.'),
    ('department',    @project,    @department, 'DepartmentId', 'Id',           'ManyToOne', N'מחלקה אחראית',   N'המחלקה האחראית על הפרויקט.');

COMMIT TRANSACTION;

SELECT CONCAT('entities=', (SELECT COUNT(*) FROM meta.Entities),
              ', fields=', (SELECT COUNT(*) FROM meta.Fields),
              ', relations=', (SELECT COUNT(*) FROM meta.Relations)) AS catalog;
GO

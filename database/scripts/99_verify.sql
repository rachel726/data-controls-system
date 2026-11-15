/* =====================================================================
   99 - Verification. Every check is PASS/FAIL; the script raises an error
   (sqlcmd -b -> exit code 1) if any check fails, so setup.ps1 can rely on it.

   Sections:
     A. row counts of the sample data and the catalog; database settings
     B. expected violation counts of the 7 sample controls (+ Stage B example)
     C. data invariants (planted violations do not overlap, baseline is clean)
     D. catalog consistency: business catalog vs. physical schema (generic, sys views only)
     E. security: the read-only principal cannot modify data or read "meta"
   ===================================================================== */
USE DataControlsDb;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

CREATE TABLE #v (n INT IDENTITY PRIMARY KEY, section CHAR(1), check_name NVARCHAR(90), expected NVARCHAR(20), actual NVARCHAR(20), details NVARCHAR(400));
DECLARE @today DATE = CAST(GETDATE() AS DATE);

/* ---------- A. row counts ---------- */
INSERT #v (section, check_name, expected, actual) VALUES
    ('A', N'departments',          '10',  (SELECT COUNT(*) FROM biz.Departments)),
    ('A', N'employees',            '222', (SELECT COUNT(*) FROM biz.Employees)),
    ('A', N'projects',             '20',  (SELECT COUNT(*) FROM biz.Projects)),
    ('A', N'catalog entities',     '3',   (SELECT COUNT(*) FROM meta.Entities)),
    ('A', N'catalog fields',       '20',  (SELECT COUNT(*) FROM meta.Fields)),
    ('A', N'catalog relations',    '6',   (SELECT COUNT(*) FROM meta.Relations)),
    ('A', N'read committed snapshot on', '1', (SELECT CAST(is_read_committed_snapshot_on AS INT) FROM sys.databases WHERE name = DB_NAME())),
    ('A', N'sample controls (active, valid JSON)', '7', (SELECT COUNT(*) FROM meta.Controls WHERE IsActive = 1 AND ISJSON(DefinitionJson, OBJECT) = 1
        AND Name IN (N'עובדים מתחת לגיל 18 עם שכר גבוה', N'תאריך סיום העסקה לפני תאריך תחילת העסקה', N'עובדים פעילים במחלקה לא פעילה',
                     N'עובדים פעילים עם נתוני חובה חסרים', N'עובדים שמרוויחים יותר מהמנהל הישיר',
                     N'פרויקטים שתאריך הסיום שלהם לפני תאריך ההתחלה', N'מחלקות פעילות ללא עובדים פעילים')));

/* ---------- B. expected violations (hand-written reference SQL, independent of the engine) ---------- */
WITH e AS (
    SELECT x.*,
           DATEDIFF(YEAR, x.BirthDate, @today) - IIF(DATEADD(YEAR, DATEDIFF(YEAR, x.BirthDate, @today), x.BirthDate) > @today, 1, 0) AS Age
    FROM biz.Employees x
)
SELECT e.Id,
    IIF(e.Age < 18 AND e.Salary > 15000, 1, 0) AS c1,
    IIF(e.TerminationDate < e.HireDate, 1, 0) AS c2,
    IIF(e.IsActive = 1 AND d.IsActive = 0, 1, 0) AS c3,
    IIF(e.IsActive = 1 AND (NULLIF(TRIM(e.Email), '') IS NULL OR NULLIF(TRIM(e.Phone), '') IS NULL OR e.DepartmentId IS NULL), 1, 0) AS c4,
    IIF(e.Salary > m.Salary, 1, 0) AS c5,
    IIF(e.IsActive = 1 AND e.Age < 18 AND e.Salary > 15000, 1, 0) AS stageB
INTO #f
FROM e
LEFT JOIN biz.Departments d ON d.Id = e.DepartmentId
LEFT JOIN biz.Employees m ON m.Id = e.ManagerId;

INSERT #v (section, check_name, expected, actual, details)
SELECT 'B', N'#1 under 18 and salary > 15,000',    '3', SUM(c1), STRING_AGG(IIF(c1 = 1, CAST(Id AS VARCHAR(10)), NULL), ',') FROM #f UNION ALL
SELECT 'B', N'#2 termination date < hire date',     '4', SUM(c2), STRING_AGG(IIF(c2 = 1, CAST(Id AS VARCHAR(10)), NULL), ',') FROM #f UNION ALL
SELECT 'B', N'#3 active employee, inactive dept',   '5', SUM(c3), STRING_AGG(IIF(c3 = 1, CAST(Id AS VARCHAR(10)), NULL), ',') FROM #f UNION ALL
SELECT 'B', N'#4 active employee missing data',     '6', SUM(c4), STRING_AGG(IIF(c4 = 1, CAST(Id AS VARCHAR(10)), NULL), ',') FROM #f UNION ALL
SELECT 'B', N'#5 earns more than own manager',      '3', SUM(c5), STRING_AGG(IIF(c5 = 1, CAST(Id AS VARCHAR(10)), NULL), ',') FROM #f UNION ALL
SELECT 'B', N'#6 project end date < start date',    '2', COUNT(*), STRING_AGG(CAST(Id AS VARCHAR(10)), ',') FROM biz.Projects WHERE EndDate < StartDate UNION ALL
SELECT 'B', N'#7 active dept without active emp.',  '1', COUNT(*), STRING_AGG(CAST(Id AS VARCHAR(10)), ',') FROM biz.Departments d
    WHERE d.IsActive = 1 AND NOT EXISTS (SELECT 1 FROM biz.Employees x WHERE x.DepartmentId = d.Id AND x.IsActive = 1) UNION ALL
SELECT 'B', N'Stage B: ACTIVE, under 18, > 15,000', '2', SUM(stageB), STRING_AGG(IIF(stageB = 1, CAST(Id AS VARCHAR(10)), NULL), ',') FROM #f;

/* ---------- C. data invariants ---------- */
INSERT #v (section, check_name, expected, actual) VALUES
    ('C', N'employees violating more than one control', '0', (SELECT COUNT(*) FROM #f WHERE c1 + c2 + c3 + c4 + c5 > 1)),
    ('C', N'baseline rows (Id<=200) violating any',     '0', (SELECT COUNT(*) FROM #f WHERE Id <= 200 AND c1 + c2 + c3 + c4 + c5 > 0)),
    ('C', N'inactive employee without termination',     '0', (SELECT COUNT(*) FROM biz.Employees WHERE IsActive = 0 AND TerminationDate IS NULL)),
    ('C', N'active employee with termination date',     '0', (SELECT COUNT(*) FROM biz.Employees WHERE IsActive = 1 AND TerminationDate IS NOT NULL)),
    ('C', N'future hire / termination dates',           '0', (SELECT COUNT(*) FROM biz.Employees WHERE HireDate > @today OR TerminationDate > @today)),
    ('C', N'distinct employee full names',              '222', (SELECT COUNT(DISTINCT CONCAT(FirstName, N' ', LastName)) FROM biz.Employees));

/* ---------- D. catalog consistency ---------- */
INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'entity table / PK column exists', '0', COUNT(*), STRING_AGG(e.[Key], ',')
FROM meta.Entities e WHERE COL_LENGTH(QUOTENAME(e.SchemaName) + '.' + QUOTENAME(e.TableName), e.PrimaryKeyColumn) IS NULL;

INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'field source column exists', '0', COUNT(*), STRING_AGG(CONCAT(e.[Key], '.', f.[Key]), ',')
FROM meta.Fields f JOIN meta.Entities e ON e.Id = f.EntityId
WHERE COL_LENGTH(QUOTENAME(e.SchemaName) + '.' + QUOTENAME(e.TableName), f.SourceColumn) IS NULL
   OR (f.TransformArg IS NOT NULL AND COL_LENGTH(QUOTENAME(e.SchemaName) + '.' + QUOTENAME(e.TableName), f.TransformArg) IS NULL);

INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'field data type matches column type', '0', COUNT(*), STRING_AGG(CONCAT(e.[Key], '.', f.[Key], '(', f.DataType, '<>', ty.name, ')'), ',')
FROM meta.Fields f
JOIN meta.Entities e ON e.Id = f.EntityId
JOIN sys.columns c ON c.object_id = OBJECT_ID(QUOTENAME(e.SchemaName) + '.' + QUOTENAME(e.TableName)) AND c.name = f.SourceColumn
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE NOT (
       (f.DataType IN ('Text', 'Enum') AND ty.name IN ('nvarchar', 'varchar', 'nchar', 'char'))
    OR (f.DataType = 'Integer' AND f.Transform = 'None' AND ty.name IN ('int', 'bigint', 'smallint', 'tinyint'))
    OR (f.DataType = 'Integer' AND f.Transform = 'YearsSince' AND ty.name IN ('date', 'datetime2', 'datetime'))
    OR (f.DataType = 'Decimal' AND ty.name IN ('decimal', 'numeric', 'money', 'float', 'real'))
    OR (f.DataType = 'Date' AND ty.name IN ('date', 'datetime2', 'datetime'))
    OR (f.DataType = 'Boolean' AND ty.name = 'bit'));

INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'entity display field exists', '0', COUNT(*), STRING_AGG(e.[Key], ',')
FROM meta.Entities e WHERE NOT EXISTS (SELECT 1 FROM meta.Fields f WHERE f.EntityId = e.Id AND f.[Key] = e.DisplayFieldKey);

INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'relation columns exist', '0', COUNT(*), STRING_AGG(CONCAT(fe.[Key], '.', r.[Key]), ',')
FROM meta.Relations r
JOIN meta.Entities fe ON fe.Id = r.FromEntityId
JOIN meta.Entities te ON te.Id = r.ToEntityId
WHERE COL_LENGTH(QUOTENAME(fe.SchemaName) + '.' + QUOTENAME(fe.TableName), r.FromColumn) IS NULL
   OR COL_LENGTH(QUOTENAME(te.SchemaName) + '.' + QUOTENAME(te.TableName), r.ToColumn) IS NULL;

WITH fk AS (
    SELECT child.Id AS ChildEntityId, parent.Id AS ParentEntityId, cc.name AS ChildColumn, pc.name AS ParentColumn,
           CONCAT(child.[Key], '.', cc.name, '->', parent.[Key]) AS label
    FROM sys.foreign_key_columns fkc
    JOIN sys.columns cc ON cc.object_id = fkc.parent_object_id AND cc.column_id = fkc.parent_column_id
    JOIN sys.columns pc ON pc.object_id = fkc.referenced_object_id AND pc.column_id = fkc.referenced_column_id
    JOIN meta.Entities child  ON OBJECT_ID(QUOTENAME(child.SchemaName)  + '.' + QUOTENAME(child.TableName))  = fkc.parent_object_id
    JOIN meta.Entities parent ON OBJECT_ID(QUOTENAME(parent.SchemaName) + '.' + QUOTENAME(parent.TableName)) = fkc.referenced_object_id
)
INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'every FK registered in both directions', '0', COUNT(*), STRING_AGG(fk.label, ',')
FROM fk
WHERE NOT EXISTS (SELECT 1 FROM meta.Relations r WHERE r.Cardinality = 'ManyToOne' AND r.FromEntityId = fk.ChildEntityId
                    AND r.ToEntityId = fk.ParentEntityId AND r.FromColumn = fk.ChildColumn AND r.ToColumn = fk.ParentColumn)
   OR NOT EXISTS (SELECT 1 FROM meta.Relations r WHERE r.Cardinality = 'OneToMany' AND r.FromEntityId = fk.ParentEntityId
                    AND r.ToEntityId = fk.ChildEntityId AND r.FromColumn = fk.ParentColumn AND r.ToColumn = fk.ChildColumn);

INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'no field/relation key collision', '0', COUNT(*), STRING_AGG(CONCAT(e.[Key], '.', f.[Key]), ',')
FROM meta.Fields f JOIN meta.Entities e ON e.Id = f.EntityId
JOIN meta.Relations r ON r.FromEntityId = f.EntityId AND r.[Key] = f.[Key];

INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'no technical (Latin) display names', '0', COUNT(*), STRING_AGG(x.name, ',')
FROM (SELECT DisplayName AS name FROM meta.Entities UNION ALL SELECT DisplayNamePlural FROM meta.Entities
      UNION ALL SELECT DisplayName FROM meta.Fields UNION ALL SELECT DisplayName FROM meta.Relations
      UNION ALL SELECT TrueText FROM meta.Fields UNION ALL SELECT FalseText FROM meta.Fields) x
WHERE x.name COLLATE Latin1_General_BIN LIKE '%[a-zA-Z]%';

INSERT #v (section, check_name, expected, actual, details)
SELECT 'D', N'every yes/no field has its Hebrew wording', '0', COUNT(*), STRING_AGG(CONCAT(e.[Key], '.', f.[Key]), ',')
FROM meta.Fields f JOIN meta.Entities e ON e.Id = f.EntityId
WHERE f.DataType = 'Boolean' AND f.TrueText IS NULL;

DECLARE @enumProblems INT = 0, @enumDetails NVARCHAR(400) = NULL, @sql NVARCHAR(MAX), @allowed NVARCHAR(MAX), @cnt INT, @label NVARCHAR(200);
DECLARE enum_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT CONCAT(e.[Key], '.', f.[Key]), f.AllowedValuesJson,
           CONCAT(N'SELECT @c = COUNT(*) FROM ', QUOTENAME(e.SchemaName), N'.', QUOTENAME(e.TableName), N' t WHERE t.', QUOTENAME(f.SourceColumn),
                  N' IS NOT NULL AND t.', QUOTENAME(f.SourceColumn), N' NOT IN (SELECT JSON_VALUE(v.value, ''$.value'') FROM OPENJSON(@allowed) v)')
    FROM meta.Fields f JOIN meta.Entities e ON e.Id = f.EntityId WHERE f.DataType = 'Enum';
OPEN enum_cur;
FETCH NEXT FROM enum_cur INTO @label, @allowed, @sql;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC sp_executesql @sql, N'@allowed NVARCHAR(MAX), @c INT OUTPUT', @allowed = @allowed, @c = @cnt OUTPUT;
    IF @cnt > 0 SELECT @enumProblems += 1, @enumDetails = CONCAT_WS(',', @enumDetails, CONCAT(@label, ':', @cnt, ' rows'));
    FETCH NEXT FROM enum_cur INTO @label, @allowed, @sql;
END
CLOSE enum_cur; DEALLOCATE enum_cur;
INSERT #v (section, check_name, expected, actual, details) VALUES ('D', N'enum data values are all allowed', '0', @enumProblems, @enumDetails);

/* ---------- E. security (no outer transaction: each check is isolated) ---------- */
DECLARE @r VARCHAR(10), @msg NVARCHAR(200);

SELECT @r = 'allowed', @msg = NULL;
EXECUTE AS USER = 'dc_query_reader';
BEGIN TRY DECLARE @x INT = (SELECT COUNT(*) FROM biz.Employees); END TRY BEGIN CATCH SELECT @r = 'denied', @msg = ERROR_MESSAGE(); END CATCH;
REVERT;
INSERT #v (section, check_name, expected, actual, details) VALUES ('E', N'read-only user: SELECT business data', 'allowed', @r, @msg);

SELECT @r = 'allowed', @msg = NULL;
EXECUTE AS USER = 'dc_query_reader';
BEGIN TRY DELETE FROM biz.Employees WHERE 1 = 0; END TRY BEGIN CATCH SELECT @r = 'denied', @msg = ERROR_MESSAGE(); END CATCH;
REVERT;
INSERT #v (section, check_name, expected, actual, details) VALUES ('E', N'read-only user: DELETE business data', 'denied', @r, LEFT(@msg, 400));

SELECT @r = 'allowed', @msg = NULL;
EXECUTE AS USER = 'dc_query_reader';
BEGIN TRY UPDATE biz.Employees SET Salary = Salary WHERE 1 = 0; END TRY BEGIN CATCH SELECT @r = 'denied', @msg = ERROR_MESSAGE(); END CATCH;
REVERT;
INSERT #v (section, check_name, expected, actual, details) VALUES ('E', N'read-only user: UPDATE business data', 'denied', @r, LEFT(@msg, 400));

SELECT @r = 'allowed', @msg = NULL;
EXECUTE AS USER = 'dc_query_reader';
BEGIN TRY DECLARE @y INT = (SELECT COUNT(*) FROM meta.Controls); END TRY BEGIN CATCH SELECT @r = 'denied', @msg = ERROR_MESSAGE(); END CATCH;
REVERT;
INSERT #v (section, check_name, expected, actual, details) VALUES ('E', N'read-only user: SELECT system config', 'denied', @r, LEFT(@msg, 400));

INSERT #v (section, check_name, expected, actual) VALUES ('E', N'identity restored after REVERT', 'dbo', USER_NAME());

/* ---------- report ---------- */
SELECT IIF(expected = actual, 'PASS', 'FAIL') AS result, section, check_name, expected, actual, details FROM #v ORDER BY n;

DECLARE @failed INT = (SELECT COUNT(*) FROM #v WHERE expected <> actual), @total INT = (SELECT COUNT(*) FROM #v);
IF @failed > 0
    RAISERROR (N'VERIFY FAILED: %d of %d checks failed.', 16, 1, @failed, @total);
ELSE
    PRINT CONCAT(N'VERIFY OK: all ', @total, N' checks passed.');
GO

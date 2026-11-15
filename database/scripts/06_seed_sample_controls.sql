/* =====================================================================
   06 - Sample controls: the 7 controls of docs/SAMPLE-CONTROLS.md, ready to run.
   Each finds exactly the violations planted by 05_seed_business_data.sql.

   Definitions are stored as JSON (a condition tree in business terms) - the same
   format the UI saves. They must stay identical to backend/tests/.../SampleDefinitions.cs
   (an integration test compares this file with them).

   Re-runnable: upserted by name; an updated sample loses its last-run result.
   ===================================================================== */
USE DataControlsDb;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

DECLARE @samples TABLE (SortOrder INT, Name NVARCHAR(200), Description NVARCHAR(1000), RootEntityKey VARCHAR(50), DefinitionJson NVARCHAR(MAX));

INSERT @samples VALUES
(1, N'עובדים מתחת לגיל 18 עם שכר גבוה',
    N'עובדים שגילם נמוך מ-18 ושכרם גבוה מ-15,000 ₪ - חשד לטעות בתאריך הלידה או בשכר.',
    'employee',
    N'{ "schemaVersion": 1, "rootEntity": "employee",
        "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "age", "operator": "lt", "operand": { "kind": "literal", "value": 18 } },
          { "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "literal", "value": 15000 } } ] } }'),

(2, N'תאריך סיום העסקה לפני תאריך תחילת העסקה',
    N'עובדים שתאריך סיום העסקתם מוקדם מתאריך תחילת העסקתם.',
    'employee',
    N'{ "schemaVersion": 1, "rootEntity": "employee",
        "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "terminationDate", "operator": "lt", "operand": { "kind": "field", "field": "hireDate" } } ] } }'),

(3, N'עובדים פעילים במחלקה לא פעילה',
    N'עובדים פעילים המשויכים למחלקה שנסגרה.',
    'employee',
    N'{ "schemaVersion": 1, "rootEntity": "employee",
        "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } },
          { "type": "condition", "field": "department.isActive", "operator": "eq", "operand": { "kind": "literal", "value": false } } ] } }'),

(4, N'עובדים פעילים עם נתוני חובה חסרים',
    N'עובדים פעילים שחסר להם דואר אלקטרוני, טלפון או שיוך למחלקה - נתונים המחויבים לפי כללי הארגון.',
    'employee',
    N'{ "schemaVersion": 1, "rootEntity": "employee",
        "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } },
          { "type": "group", "logic": "or", "children": [
            { "type": "condition", "field": "email", "operator": "isEmpty" },
            { "type": "condition", "field": "phone", "operator": "isEmpty" },
            { "type": "condition", "field": "department.name", "operator": "isEmpty" } ] } ] } }'),

(5, N'עובדים שמרוויחים יותר מהמנהל הישיר',
    N'עובדים ששכרם גבוה משכר המנהל הישיר שלהם.',
    'employee',
    N'{ "schemaVersion": 1, "rootEntity": "employee",
        "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "field", "field": "manager.salary" } } ] } }'),

(6, N'פרויקטים שתאריך הסיום שלהם לפני תאריך ההתחלה',
    N'פרויקטים עם טווח תאריכים הפוך.',
    'project',
    N'{ "schemaVersion": 1, "rootEntity": "project",
        "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "endDate", "operator": "lt", "operand": { "kind": "field", "field": "startDate" } } ] } }'),

(7, N'מחלקות פעילות ללא עובדים פעילים',
    N'מחלקות שמוגדרות פעילות אך אין בהן אף עובד פעיל.',
    'department',
    N'{ "schemaVersion": 1, "rootEntity": "department",
        "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } },
          { "type": "related", "relation": "employees", "quantifier": "none",
            "where": { "type": "group", "logic": "and", "children": [
              { "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } } ] } } ] } }');

-- update existing samples (by name), clearing their last run - it no longer reflects the definition
UPDATE c
SET Description = s.Description, RootEntityKey = s.RootEntityKey, DefinitionJson = s.DefinitionJson, UpdatedAt = SYSUTCDATETIME(),
    LastRunAt = NULL, LastRunStatus = NULL, LastViolationCount = NULL, LastRunError = NULL
FROM meta.Controls AS c
JOIN @samples AS s ON s.Name = c.Name
WHERE c.IsActive = 1;

-- insert the missing ones
INSERT meta.Controls (Name, Description, RootEntityKey, DefinitionJson)
SELECT s.Name, s.Description, s.RootEntityKey, s.DefinitionJson
FROM @samples AS s
WHERE NOT EXISTS (SELECT 1 FROM meta.Controls c WHERE c.Name = s.Name AND c.IsActive = 1)
ORDER BY s.SortOrder;

COMMIT TRANSACTION;

SELECT CONCAT('sample controls: ', COUNT(*)) AS seeded
FROM meta.Controls c WHERE c.IsActive = 1 AND c.Name IN (SELECT Name FROM @samples);
GO

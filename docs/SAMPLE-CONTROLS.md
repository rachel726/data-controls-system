# Sample controls

The setup seeds a small company — 10 departments, 222 employees, 20 projects — mostly valid, with **planted violations
of exact counts**, and 7 controls that find them. The counts are checked after setup and by integration tests.

| # | Control | Shows | Violations |
|---|---|---|---|
| 1 | עובדים מתחת לגיל 18 עם שכר גבוה | AND, a computed field (age) | **3** |
| 2 | תאריך סיום העסקה לפני תאריך תחילת העסקה | comparison between two fields | **4** |
| 3 | עובדים פעילים במחלקה לא פעילה | a related record's field | **5** |
| 4 | עובדים פעילים עם נתוני חובה חסרים | an OR group, empty values | **6** |
| 5 | עובדים שמרוויחים יותר מהמנהל הישיר | comparison with the manager's record | **3** |
| 6 | פרויקטים שתאריך הסיום שלהם לפני תאריך ההתחלה | another kind of record | **2** |
| 7 | מחלקות פעילות ללא עובדים פעילים | "there are no" related records | **1** |

Each violation is listed with its reason, built from the conditions it met and the record's actual values.

## A new control — no code

Open **בקרה חדשה**, choose what to check, add conditions (field, operator, value), save and run. It is saved as data:

```json
{ "schemaVersion": 1, "rootEntity": "employee",
  "filter": { "type": "group", "logic": "and", "children": [
    { "type": "condition", "field": "age",    "operator": "lt", "operand": { "kind": "literal", "value": 18 } },
    { "type": "condition", "field": "salary", "operator": "gt", "operand": { "kind": "literal", "value": 15000 } } ] } }
```

## A new field or table — no code

A field is one catalog row — e.g. seniority, computed from the hire date:

```sql
INSERT meta.Fields (EntityId, [Key], DisplayName, Description, SourceColumn, DataType, Transform,
                    IsRequiredByBusiness, IsVisibleInResults, IsSearchable, SortOrder)
SELECT Id, 'seniority', N'ותק בשנים', N'שנים מלאות מתחילת ההעסקה.', 'HireDate', 'Integer', 'YearsSince', 0, 0, 0, 13
FROM meta.Entities WHERE [Key] = 'employee';
```

It appears in the editor after the catalog refreshes, and a control on it runs at once (tried live: seniority of 10
years or more — 92 of 222 employees). A new table = the table in schema `biz` + rows in `meta.Entities`, `meta.Fields` and `meta.Relations`;
`99_verify.sql` confirms the catalog matches the database.

# PLAN — Dynamic Data Controls System (מערכת בקרות נתונים דינאמית)

> Single source of truth for architecture, contracts and conventions.
> Every work session reads this file (§1–§5, §9 + the section relevant to its task) before coding.
> If a session must change a contract defined here, it updates this file in the same task and notes it in PROGRESS.md.
> **Scope rule:** build only what the assignment asks (§10). Anything else goes to "possible extensions" in the docs, not into code.

---

## 1. Goal

A web system that lets **non-technical business users** define data-quality controls (בקרות) over a relational database
using **business terms only**, save/edit/reuse them, run them, and inspect the violating records (חריגות) with a
**reason per record**. Controls are stored as **data** (a JSON condition tree), never as code or SQL. A generic engine
translates definition → logical query model → parameterized, read-only SQL Server query.
Stage B: a free-text → control-definition flow through a pluggable AI provider (Mock only), decoupled from the engine.

**Violation (חריגה)** = a record of the control's root entity that MATCHES the control's filter
(the filter describes the problematic situation, e.g. "age < 18 AND salary > 15,000").

---

## 2. Tech stack & key decisions

| Area | Choice | Why |
|---|---|---|
| Backend API | .NET 10 Web API (Controllers) | Required |
| Data access | Dapper + Microsoft.Data.SqlClient | SQL is generated dynamically; full control over parameters; EF adds nothing |
| Query building | Own Validator → Planner → Renderer | Per-condition flags for reasons, catalog-whitelisted identifiers, clear definition/translation/execution separation (the core of the assignment) |
| AI service | Python 3.12 + FastAPI + Pydantic v2 | Required Python; natural home for AI integration; stateless |
| Frontend | Angular 22, standalone components, signals, Angular Material, `dir="rtl"`, Hebrew | Required |
| State | Signal-based feature stores | Idiomatic modern Angular; no NgRx needed at this size |
| DB | SQL Server 2022 Express `.\SQLEXPRESS`, Windows auth | Required |
| Tests | xUnit + Shouldly (.NET), pytest (Python), Angular CLI default runner | Evaluation: tests & edge cases |

Ports (dev): API `http://localhost:5080`, Angular `http://localhost:4200` (proxy `/api` → 5080), AI service `http://localhost:8001`.
Angular talks only to the .NET API. The .NET API is the only caller of the Python service and the only component touching the DB.

---

## 3. Architecture

```
Angular UI ──HTTP/JSON──► .NET API
                           ├ Api            controllers, ProblemDetails
                           ├ Application    use-case services, DTOs, AI suggestion mapping
                           ├ Core (pure)    Catalog · Definitions · Validation · Planning · Sql · Describing
                           └ Infrastructure Dapper repos · read-only QueryExecutor · PythonAiServiceClient
                                │ SQL (parameters, EXECUTE AS read-only user)      │ HTTP/JSON
                                ▼                                                  ▼
                           SQL Server DataControlsDb                         Python ai-service (FastAPI)
                             biz  = sample business data                       PromptBuilder → LlmProvider (Mock)
                             meta = catalog + controls                         → Pydantic-validated suggestion
```

### 3.1 Control pipeline
```
ControlDefinition (JSON in meta.Controls)
  1. DefinitionValidator   fields exist (via catalog), operator ⟷ type, operand types, limits
  2. QueryPlanner          field paths ("department.isActive") → join chain via catalog relations; dedupe joins;
                           predicate tree; one flag per leaf condition; select list
     → QueryModel (logical, DB-agnostic)
  3. SqlServerRenderer     identifiers only from catalog (bracket-quoted), every value a typed parameter,
                           paging / search / column filters / sort wrapper, count query
     → RenderedQuery { Sql, Parameters }
  4. QueryExecutor         EXECUTE AS read-only user, timeout, row cap
  5. ViolationReasonBuilder flags + values + catalog → Hebrew reason per row
```
Assignment separation: definition (1) ≠ translation (2–3) ≠ execution (4).
`DefinitionDescriber` turns a definition into a Hebrew sentence (editor preview + AI approval screen).

### 3.2 Why it's generic
- No business table/column name appears in C#/Python/TS code — everything comes from the `meta` catalog.
- New control = UI only, zero code. New business table/field = rows in `meta.Entities/Fields/Relations`, zero code.

---

## 4. Database (`DataControlsDb`)

### 4.1 Schema `biz` — sample business data
No business CHECK constraints on purpose (bad data must be able to exist — that is what controls detect).

| Table | Columns |
|---|---|
| `biz.Departments` | Id PK, Name, Code, IsActive bit |
| `biz.Employees` | Id PK, EmployeeNumber, FirstName, LastName, BirthDate NULL, Email NULL, Phone NULL, HireDate, TerminationDate NULL, Salary decimal(10,2) NULL, IsActive bit, DepartmentId NULL FK, ManagerId NULL FK→Employees (self) |
| `biz.Projects` | Id PK, Name, DepartmentId FK, StartDate, EndDate NULL, Budget decimal(14,2) NULL, Status ('Planned','Active','Completed','Cancelled') |

Indexes: every FK column; `Employees(DepartmentId) INCLUDE (IsActive)` (serves the join and the "department without active
employees" EXISTS); `Employees(BirthDate)` (sargable age). No standalone `IsActive` index — a bit column is too unselective.
DB collation `Hebrew_100_CI_AS` (explicit, server-independent).

Seed: ~10 departments (2 inactive), ~200 employees, ~20 projects. Deterministic set-based T-SQL. Mostly valid data +
**planted violations with exact, non-overlapping counts** (documented in `docs/SAMPLE-CONTROLS.md`):

| # | Control (sample) | Shows | Count |
|---|---|---|---|
| 1 | Employees under 18 with salary > 15,000 (2 of the 3 are active — Stage B example) | AND, computed field (age) | 3 |
| 2 | Termination date earlier than hire date | field-to-field | 4 |
| 3 | Active employees in an inactive department | relation (join) | 5 |
| 4 | Active employees missing business-required data (email OR phone OR department empty) | OR group nested in AND | 6 |
| 5 | Employee earns more than their own manager | self-relation + field-to-field across join | 3 |
| 6 | Project end date earlier than start date | another root entity | 2 |
| 7 | Active department with no active employees | one-to-many relation (`NOT EXISTS`) | 1 |

### 4.2 Schema `meta` — semantic catalog + controls

| Table | Columns |
|---|---|
| `meta.Entities` | Id, **Key** (`employee`), DisplayName (`עובד`), DisplayNamePlural (`עובדים`), **Gender** (`Masculine|Feminine`, Hebrew wording "בהם/בהן"), Description, SchemaName, TableName, PrimaryKeyColumn, DisplayFieldKey, SortOrder |
| `meta.Fields` | Id, EntityId FK, **Key** (`age`), DisplayName (`גיל`), Description, SourceColumn (`BirthDate`), DataType (`Text|Integer|Decimal|Date|Boolean|Enum`), Transform (`None|YearsSince|FullName`), TransformArg NULL (second column for FullName), IsRequiredByBusiness, IsVisibleInResults, IsSearchable, AllowedValuesJson NULL (`[{"value":"Active","label":"פעיל"}]`), Unit NULL (`₪`), TrueText/FalseText NULL (Boolean only, both or neither: `פעיל`/`אינו פעיל` → "עובד פעיל"), SortOrder |
| `meta.Relations` | Id, **Key** (`department`), FromEntityId, ToEntityId, FromColumn, ToColumn, **Cardinality** (`ManyToOne|OneToMany`), DisplayName (`מחלקה`), Description. ManyToOne: employee.department, employee.manager, project.department. OneToMany: department.employees, department.projects, employee.directReports |
| `meta.Controls` | Id, Name (unique), Description, RootEntityKey, **DefinitionJson** (ISJSON check), IsActive (soft delete), CreatedAt, UpdatedAt, LastRunAt NULL, LastRunStatus NULL (`Succeeded|Failed`), LastViolationCount NULL, LastRunError NULL |

Last-run columns: the list screen shows the violation count found and failed runs are recorded — no separate run-history table.

### 4.3 DB security
- `dc_query_reader` — database user **WITHOUT LOGIN**: `GRANT SELECT ON SCHEMA::biz`, `DENY` on `meta`, nothing else.
- Executor runs each generated query as `EXECUTE AS USER = 'dc_query_reader'; BEGIN TRY <SELECT…> REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH`
  → even an engine bug cannot modify data or read `meta`. Works with Windows auth. (Production alternative documented: dedicated read-only login.)
- Executor robustness (found by tests in 3.3): remaining result sets are drained so a CATCH-raised error is never mistaken for
  "no violations"; on ANY SqlException the connection pool is cleared (a timeout leaves the session in "kill" state, a
  batch-aborting error skips REVERT); exceeding the row cap is an error, never a silently truncated result.

### 4.4 Scripts (`database/scripts`, run by `database/setup.ps1`, `-Force` drops & recreates)
`00_create_database.sql` · `01_biz_schema.sql` · `02_meta_schema.sql` · `03_security.sql` · `04_seed_catalog.sql` ·
`05_seed_business_data.sql` · `06_seed_sample_controls.sql` · `99_verify.sql` (row counts + expected violation counts).

---

## 5. Contracts (binding across .NET / Python / Angular)

### 5.1 ControlDefinition JSON (`meta.Controls.DefinitionJson`)
Field path = relation keys then a field key, relative to the root entity: `age`, `department.isActive`, `manager.salary`.
```jsonc
{
  "schemaVersion": 1,
  "rootEntity": "employee",
  "filter": {                                   // root is always a group
    "type": "group", "logic": "and",            // "and" | "or"
    "children": [
      { "type": "condition", "field": "age", "operator": "lt",
        "operand": { "kind": "literal", "value": 18 } },
      { "type": "condition", "field": "terminationDate", "operator": "lt",
        "operand": { "kind": "field", "field": "hireDate" } },     // field-to-field
      { "type": "condition", "field": "email", "operator": "isEmpty" },
      { "type": "condition", "field": "salary", "operator": "between",
        "operand": { "kind": "literal", "value": [5000, 9000] } },
      { "type": "group", "logic": "or", "children": [ /* nested */ ] }
    ]
  }
}
```
Related-records condition (OneToMany relations only; `where` paths are relative to the related entity):
```jsonc
{ "type": "related", "relation": "employees", "quantifier": "none",   // "any" | "none"
  "where": { "type": "group", "logic": "and",
             "children": [ { "type": "condition", "field": "isActive", "operator": "eq",
                             "operand": { "kind": "literal", "value": true } } ] } }
```
Plain field paths may only traverse ManyToOne relations; OneToMany is reachable only through a `related` node.
C#: `[JsonPolymorphic]` on `type` / `kind`, camelCase, enums as strings. TS: discriminated unions.
Python does NOT model this contract — it has its own AI response contract (§7) mapped by .NET.

### 5.2 Operators (single source: `OperatorRegistry`, served by `GET /api/catalog/operators`)

| Key | Hebrew | Types | Operand |
|---|---|---|---|
| eq / neq | שווה ל / שונה מ | all | literal / field |
| gt / gte / lt / lte | גדול מ / גדול או שווה ל / קטן מ / קטן או שווה ל (dates: אחרי / … / לפני) | Integer, Decimal, Date | literal / field |
| between | בין | Integer, Decimal, Date | literal [a,b] |
| in | אחד מ | Text, Integer, Enum | literal array (≤100) |
| contains | מכיל | Text | literal |
| isEmpty / isNotEmpty | ריק / לא ריק | all (Text: NULL or blank) | none |

NULL semantics: comparisons are false on NULL; `neq` includes NULL (`col <> @p OR col IS NULL`); `isEmpty` = NULL or blank text.

### 5.3 Validation limits (`ValidationLimits`)
Depth ≤3 (a `related` node counts as a level) · ≤20 leaf conditions · ≤1 relation hop per path (configurable; one keeps the field list short and covers every example) · `related` not nested inside another `related` · IN ≤100 · page size ≤200 · command timeout 30s.
One `ValidationLimits` instance (configuration section `Validation`) holds every limit on user input - the above plus
text value 200, search / filter 100, name 200, description 1000, AI request 1000 - and is read by the validator, the
planner, the services and the SQL renderer, served to the UI by `GET /api/catalog/limits` and passed to the AI as
`context.maxRelationHops` — no client or service repeats a limit.
Errors: `{ path: "filter.children[1].operand", code: "OperatorNotAllowedForType", message: "<Hebrew>", isIncomplete: false }` -
`isIncomplete` = only not filled in yet (no field / value, an empty list or group): the editor does not mark it while editing.

### 5.4 Field transforms (closed set in the renderer — never free SQL in metadata)
| Transform | Display SQL | Filter SQL |
|---|---|---|
| `None` | `[a].[Col]` | `[a].[Col] op @p` |
| `YearsSince` (`age` ← BirthDate) | exact age at `@today` | sargable: `age < N` ⇔ `BirthDate > DATEADD(year,-N,@today)` etc. (index on BirthDate is used) |
| `FullName` | `CONCAT(first,' ',last)` | same expr |
`@today` is a single parameter → deterministic & testable.

### 5.5 REST API — errors are RFC 7807 ProblemDetails with Hebrew `title`/`detail`, `errors` for validation
JSON contract for the whole API = the definition contract (`DefinitionSerializer.ApplyContract`): camelCase properties,
**camelCase string enums** (`"integer"`, `"succeeded"`, `"manyToOne"`), unknown properties rejected.
Errors: 400 input (`errors` per field) / invalid definition (`definitionErrors[]` with node paths) / bad result options,
404 not found, 409 duplicate name, 504 query timeout, 500 generic (no internals); all with `traceId`.

| Method & route | Purpose |
|---|---|
| `GET /api/catalog/entities` | Entities with fields + relations (business view) + `fieldPaths`: the fields a control on the entity may use (`{ path, label, owner, entityKey, fieldKey }`), listed and named by the server's path resolver - the UI only displays them |
| `GET /api/catalog/operators` | Operator registry |
| `GET /api/catalog/limits` | `{ maxConditions, maxDepth, maxRelationHops, maxNameLength, maxDescriptionLength, maxAiTextLength }` (§5.3) |
| `GET /api/controls?search=` | List incl. last-run summary |
| `GET /api/controls/{id}` | Full control incl. definition |
| `POST /api/controls` | Create (validates) → 201 |
| `PUT /api/controls/{id}` | Update (validates) |
| `DELETE /api/controls/{id}` | Soft delete |
| `POST /api/controls/validate` | definition → `{ isValid, errors[], description }` |
| `POST /api/controls/{id}/run` | Execute, update last-run columns → `RunResult` (first page) |
| `POST /api/controls/{id}/results` | `{ page, pageSize, search, sort:{field,dir}, columnFilters:[{field,value}], includeSummary }` → `RunResult` page; `includeSummary: false` (paging / sorting / filtering / searching) skips the summary counts → `violationCount` / `totalScanned` null |
| `GET /api/ai/examples` | `[{ label, text }]` - one-click examples, served by the AI service (`GET /v1/examples`; the mock: its fixtures with an `example` label) |
| `POST /api/ai/suggest-control` | `{ text }` → `AiSuggestionResult` (§7) |

`RunResult`:
```jsonc
{ "controlId": 3, "executedAt": "...", "durationMs": 41, "violationCount": 3, "totalScanned": 200, "totalFiltered": 3,
  "page": 1, "pageSize": 25,
  "columns": [ { "key": "fullName", "displayName": "שם מלא", "dataType": "text", "unit": null, "isConditionField": false } ],
  "rows": [ { "recordId": 1043, "title": "יוסי כהן", "values": { "fullName": "יוסי כהן", "age": "16", "salary": "22,000 ₪" },
              "reasons": [ "גיל (16) קטן מ-18", "שכר (22,000 ₪) גדול מ-15,000 ₪" ] } ] }
```
Column keys with dots are flattened with `__` (`department__name`).

### 5.6 Reasons
Each leaf condition `i` gets `[__c{i}] = CASE WHEN <pred> THEN 1 ELSE 0 END` plus the referenced values.
A `related` node is one leaf with one flag; its reason is its Hebrew description (e.g. "אין עובדים פעילים במחלקה").
`ViolationReasonBuilder` lists only conditions with flag 1 (an OR shows the branch that fired), formatted by `HebrewFormat`
(thousands separators, dd/MM/yyyy, כן/לא, enum labels, units, "ריק" for NULL).

### 5.7 Query shape
```sql
WITH v AS (
  SELECT r.[Id] AS [__id], <display cols>, <flag cols>
  FROM [biz].[Employees] AS r
  LEFT JOIN [biz].[Departments] AS j1 ON j1.[Id] = r.[DepartmentId]
  WHERE <predicate tree>
)
SELECT *, COUNT(*) OVER() AS [__total] FROM v
WHERE <search over searchable text cols LIKE @s> AND <column filters>
ORDER BY <whitelisted column> OFFSET @off ROWS FETCH NEXT @take ROWS ONLY;
```
`related` renders as `[NOT] EXISTS (SELECT 1 FROM [biz].[Employees] AS x1 WHERE x1.[DepartmentId] = r.[Id] AND <where>)`
— a semi-join that uses the FK index and never multiplies root rows (unlike a JOIN to the "many" side).
Plus `SELECT COUNT(*)` on the root table for `totalScanned`. LIKE values escape `%`, `_`, `[`.

---

## 6. Solution layout
```
data-controls-system/
├─ CLAUDE.md PLAN.md PROGRESS.md README.md run-all.ps1 .gitignore .editorconfig
├─ database/  setup.ps1  scripts/*.sql
├─ backend/   DataControls.slnx
│  ├─ src/DataControls.Api/            Program.cs · Controllers/{Catalog,Controls,Ai}Controller.cs · ErrorHandling/GlobalExceptionHandler.cs
│  ├─ src/DataControls.Application/    Catalog/ · Controls/{ControlService,ControlRunService} · Ai/{AiSuggestionService,SchemaContextBuilder,AiSuggestionMapper} · Dtos/ · Exceptions/
│  ├─ src/DataControls.Core/           Catalog/ · Definitions/ · Operators/ · Paths/ (shared by validator + planner) · Validation/ · Planning/ · Sql/ · Describing/ · Abstractions/
│  ├─ src/DataControls.Infrastructure/ Data/ · Repositories/ · Execution/SqlQueryExecutor · Catalog/CachedCatalogProvider · Ai/PythonAiServiceClient
│  └─ tests/DataControls.Core.Tests · tests/DataControls.IntegrationTests (real local DB)
├─ ai-service/  app/{main.py,config.py,api/,models/contracts.py,prompting/,providers/{base,mock_provider,factory}.py,providers/mock_fixtures/,services/}  tests/
├─ frontend/    src/app/{core,layout,shared/components,features/{controls/{list,editor,results},ai}}
└─ docs/        ARCHITECTURE.md · SAMPLE-CONTROLS.md · AI-STAGE-B.md · ASSUMPTIONS-LIMITATIONS.md · AI-TOOLS-USED.md · ai-response.schema.json
```

---

## 7. Stage B — AI infrastructure

**Flow:** UI free text → `POST /api/ai/suggest-control` → .NET `SchemaContextBuilder` builds a business schema context
from the catalog (entities, fields with Hebrew names/descriptions/types/allowed values, relations, operators, the
relation-path limit `maxRelationHops` — **no data rows**) → `IControlSuggestionClient` (`PythonAiServiceClient`, typed HttpClient + timeout) → Python
`POST /v1/suggest-control` → `PromptBuilder` (instructions + context + expected JSON schema) → `LlmProvider.generate(prompt) -> str`
(raw text like a real model) → Pydantic validation → .NET `AiSuggestionMapper` **translates the AI response into a
Stage A `ControlDefinition`** → same `DefinitionValidator` as manual controls + `DefinitionDescriber` → UI shows it for
**approval** → user edits in the regular editor, or approves & saves (& runs). Nothing runs without validation + approval.

**AI response contract** (`ControlSuggestion`, own format, decoupled from the engine; JSON Schema in `docs/ai-response.schema.json`):
```jsonc
{ "name": "...", "description": "...", "rootEntity": "employee",
  "logic": "and",
  "conditions": [ { "field": "age", "operator": "lt", "value": 18 },
                  { "field": "terminationDate", "operator": "lt", "compareToField": "hireDate" } ],
  "groups": [ { "logic": "or", "conditions": [ ... ] } ],   // optional, one nesting level
  "explanation": "איך הבנתי את הבקשה", "assumptions": ["..."], "unmappedTerms": [] }
```
`AiSuggestionResult` (.NET → UI) = `understood` + suggestion text fields (`name`, `description`, `explanation`, `assumptions`, `unmappedTerms`) + mapped `definition` + `{ isValid, validationErrors[], definitionDescription }` (the Hebrew meaning - named like `ControlDto.definitionDescription`, since `description` is the suggestion's own text). AI service down / timed out → 503; its answer outside the contract → 502.

**Provider:** `LlmProvider` protocol + `factory` (env `AI_PROVIDER`, only `mock` implemented). `MockLlmProvider` returns
fixture JSON for the Stage B example and the 4 assignment examples (matched by normalized text); one fixture
(`"דמה שגיאה"`) returns an unknown field to demo the validation path; anything else → empty conditions + `unmappedTerms`
(UI says "לא הצלחתי להבין"). Swapping a model = new provider class + env var; .NET engine and UI unchanged.

**Optional advanced task (design only, as the assignment asks "תכנון"):** a section in `docs/AI-STAGE-B.md` describing
how the AI would analyze the catalog + safe aggregate data profile and proactively propose controls. Not implemented.

---

## 8. Frontend

- Shell: toolbar "בקרות נתונים", nav: "הבקרות שלי", "בקרה חדשה", "בקרה בשפה חופשית". `<html lang="he" dir="rtl">`, font Heebo, Material M3.
- Routes (lazy): `/controls` · `/controls/new` · `/controls/:id/edit` · `/controls/:id/results` · `/ai`.
- **core/**: models (mirror §5), API services, `error.interceptor.ts` (ProblemDetails → Hebrew snackbar), `catalog.store.ts`.
- **shared/components (reusable):** `page-header`, `empty-state`, `confirm-dialog`, `field-picker` (grouped by entity,
  related fields shown as "מחלקה ← פעילה", description tooltip), `operator-select`, `value-input` (by type: number/date/
  boolean/enum/chips for IN/range for between/"השווה לשדה אחר"), `condition-group` (recursive, read-only mode for AI preview),
  `related-row` ("יש לפחות" / "אין אף" + relation, e.g. "עובדים", + nested `condition-group` on the related entity),
  `definition-description`, `data-table` (server columns, paging, sort, search, column filters, card layout < 600px).
- **features/controls:** list page (search, run ▶, edit, delete, last violation count / failed badge, empty state) ·
  editor page + `control-editor.store.ts` (signals; immutable updates by node path) + side panel with live Hebrew
  description and errors (debounced `/validate`) · results page (summary: violations / scanned / duration; table with reasons; search; filters).
- **features/ai:** textarea + example chips → result review (Hebrew sentence, explanation, assumptions, validation errors,
  read-only conditions) → "ערוך בעורך" / "אשר ושמור" / "אשר, שמור והרץ".
- UX rules: business language only, Hebrew messages, loading/empty/error states, responsive ≥360px.
- Wording that removes the "results vs. errors" ambiguity: the condition builder is titled **"רשומה תיחשב חריגה כאשר:"**;
  results page says **"נמצאו N רשומות חריגות מתוך M"** (data problems). System/technical errors (invalid control, DB timeout)
  are shown separately as an error message, never as results.

---

## 9. Conventions
- C#: nullable, file-scoped namespaces, records for DTOs/models, async + CancellationToken, DI, Options pattern.
- Logging: generated SQL at Debug without parameter values; run summary at Information.
- Errors: domain exceptions mapped centrally (400 invalid definition, 404, 503 AI service down, 504 query timeout, 500 generic without internals).
- Python: type hints, Pydantic v2, ruff-clean. Angular: standalone, OnPush, signals, no `any`, Hebrew UI strings.
- Git: one commit per task `Task X.Y: <title>`.
- No business table/column names outside `database/scripts`, test fixtures and docs.

---

## 10. Requirement traceability (assignment → plan)

| Assignment requirement | Where |
|---|---|
| Sample DB, several related tables, valid + invalid data | §4.1 (3 tables, FKs incl. self-relation, 7 planted violation sets) |
| Engine generic, no hardcoded tables/fields | §3.2 catalog-driven |
| UI to create a control without SQL / schema knowledge | Editor + field-picker in business terms (§8) |
| Name + description | Editor header |
| Choose business data | Root entity select + related fields via relations |
| Logical conditions incl. combinations | Nested AND/OR groups, field-to-field (§5.1) |
| Save & edit | POST/PUT (§5.5) |
| Reuse controls | Saved list, run again any time |
| Friendly display of data & relations in business terms | Catalog display names, relation paths, descriptions, Hebrew sentence |
| Generic definition → SQL, dynamic | Validator → Planner → Renderer (§3.1) |
| Complex conditions & table relations | Nested groups, multi-hop joins (many-to-one), self-relation, related-records conditions (one-to-many, EXISTS) |
| Separate definition from query implementation | QueryModel layer + renderer |
| Prevent dangerous commands / data change | No user SQL; whitelisted identifiers; parameters; SELECT-only renderer; EXECUTE AS read-only user; limits & timeout |
| Error handling & messages | ProblemDetails in Hebrew, validation paths, UI interceptor |
| No fixed query per control | One generic renderer |
| Results: list, run from list, count, table, reason, filter & search | List page + results page, reasons (§5.6) |
| Clear, convenient, responsive UI | §8 |
| Stage B: free-text input | `/ai` page |
| Pass schema & business meaning to AI | SchemaContextBuilder → PromptBuilder |
| Define expected AI response | `ControlSuggestion` + JSON Schema |
| Mock AI response | MockLlmProvider |
| Translate AI response → Stage A definition | AiSuggestionMapper + same validator |
| Show for approval before running | AI page review + actions |
| AI decoupled, model swappable | LlmProvider strategy + IControlSuggestionClient |
| Optional: AI proposes controls from schema (design) | Design section in AI-STAGE-B.md |
| .NET Web API, Python, layered architecture, SQL Server, secure dynamic SQL | §2, §3 |
| Angular, Hebrew RTL, reusable components, business UX | §8 |
| DB: sample tables, controls storage, success/failure data | §4 |
| Deliverables: code, DB scripts, install/run guide, architecture doc, sample controls, Stage B Mock demo, assumptions/limits/extensions, AI tools used | README + docs (task 7.3) |
| Evaluation: tests & edge cases | Core unit tests, integration tests per sample control, pytest, a few Angular tests |

**Deliberately out of scope (mentioned as extensions in docs only):** authentication/roles, run history & result snapshots,
scheduling/alerts, export, numeric aggregates over one-to-many relations (sum/avg — only exists/not-exists is implemented), relative dates ("today"), optimistic
concurrency, CI/Docker, real AI provider.

## 11. Environment notes
- Toolchain (task 0.1): .NET 10.0.404, Python 3.12.10, Node 26.13.1, Angular CLI 22.2.7, SQL Server 2022 Express. Caches on `D:\dev-cache`. No Docker.
- Project folder: `mfe-interview-lab\data-controls-system` (opened from the parent VS Code workspace — read CLAUDE.md explicitly).

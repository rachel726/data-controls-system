# Working assumptions, limitations and possible extensions

## Working assumptions

- **A control describes the problem**; a record that matches it is a violation — how business users phrase controls.
- **One kind of record per control** (employees, departments, projects); conditions may use related records' fields.
- **Empty values:** "ריק" finds missing data; comparisons never match an empty value ("salary over 15,000" never
  reports an unknown salary).
- **Dates:** "today" is the server's date at run time; age = whole years since the birth date.
- **Roles:** business users create controls; an administrator maintains the catalog with SQL.
- **Stage B:** no real AI call, as the assignment states — the Mock stands in for the model ([AI-STAGE-B.md](AI-STAGE-B.md)).

## Limitations

| Area | Limitation |
|---|---|
| Users | No authentication or roles |
| Runs | Only the latest run is kept; no history |
| Conditions | No counts or sums over related records; no relative dates ("last 30 days") |
| Limits | 20 conditions, nesting 3, one relation per field, 100 list values, 30 s query timeout — all configurable |
| Stage B | The Mock knows the assignment's examples (with any values) and simple requests; free phrasing needs a real model |
| Catalog | Edited with SQL, no admin screen; cached for 10 minutes |
| Concurrency | The last save wins |
| Operations | Local setup — no TLS, rate limiting or CI pipeline; production needs a dedicated database login with minimal rights |

## Possible extensions

1. Authentication and roles; controls per team.
2. Scheduled runs, history, trends, alerts and export to Excel.
3. Counts and sums over related records; relative dates.
4. A catalog administration screen with built-in consistency checks.
5. A real AI model behind the existing interface, and the proactive suggestions designed in [AI-STAGE-B.md](AI-STAGE-B.md) §6.
6. Containers and a CI pipeline running all tests and database checks.
7. Other databases — another renderer behind the same query model.

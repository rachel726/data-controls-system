# Working rules for every session in this repo

This project is built **one task per chat**. The user will say e.g. "בצע משימה 2.4" / "Do task 2.4".

1. Read `PLAN.md` §1–§5 and §9, plus the PLAN sections your task touches. Read **only your task block** in `PROGRESS.md` and the status table.
2. Verify every "Depends on" task is ✅/⚠️. If not — stop and tell the user.
3. Mark the task 🟨 in the status table, then do **only that task**. Don't refactor unrelated code; don't start the next task.
4. Read existing code only as needed (targeted Grep/Read, not whole-repo scans).
5. Contracts in PLAN §5 are binding. If you must change one, update PLAN.md in the same task and mention it in the report.
6. Verify before reporting: build, run tests, and run/curl what you built. Never mark ✅ with failing build/tests — use ⚠️ and explain.
7. Finish by:
   - filling the task's **Report** in PROGRESS.md: what was done, files created/changed, how it was verified (commands + result), deviations, follow-ups for later tasks;
   - setting status ✅ (or ⚠️);
   - `git commit` with message `Task X.Y: <title>`;
   - a short Hebrew summary to the user + the exact prompt for the next task.
8. Ask the user before installing software, deleting files, or anything outside this folder.

## Engineering standards (permanent — apply to every task)

- **Senior full-stack quality.** Correct architecture, clean and maintainable code, **generic**: no business table/field names in code — everything comes from the catalog; a new control/entity must work without a code change.
- **Scope vs. quality.** Build only what the assignment requires — no unnecessary features or complexity. Build each required thing the most professional, smart and efficient way, not merely the simplest.
- **Root causes only.** Never a local patch that hides a deeper problem; never break existing behaviour (run the existing tests).
- **Never assume it works.** Compiling is not enough: after every significant change run the relevant tests **and** exercise the feature end to end for real (DB / API / UI — screenshots or measurements for UI). Self-review new code before running it; mutation-check important tests.
- **Check every change for** edge cases, performance, security, error handling and UX (Hebrew RTL, responsive ≥360px, stable layout without jumps).
- **Report honestly**: what was done, which checks passed, what could not be verified, what is still open.
- **Hebrew for everything the user reads** — final replies **and** the short explanations between tool calls while working. Code, commits and docs stay English.

Environment: Windows 11, PowerShell + Git Bash, SQL Server Express `.\SQLEXPRESS` (Windows auth), C: drive is low on space — keep caches on D: if configured.
UI language: Hebrew, RTL. Code, identifiers, commits, docs: English (except user-facing strings and docs/AI-TOOLS-USED.md may be bilingual).

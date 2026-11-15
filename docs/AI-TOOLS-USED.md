# AI tools used / כלי AI שבהם נעשה שימוש

## Claude Code (Anthropic, Claude Opus) - an AI coding agent

I built this project with an agentic workflow: Claude Code worked inside the repository as an autonomous agent - writing
code, running builds, tests, database scripts and a headless browser - while I worked with it the way a tech lead works
with a developer: I defined the work, set the standards, reviewed every result and made the decisions.

**What I owned**

- **Requirements and priorities.** I read the assignment as a test of extensibility, not of screens, and set the rule
  for the whole project: *scope decides what is built - only what is required; quality decides how - the most
  professional way*. The assignment was broken into small tasks with dependencies and contracts (`PLAN.md`), worked one
  at a time, each closed with a verification report and a commit (`PROGRESS.md`).
- **Architecture.** I set the principles the design follows: fully generic - no table or field name in code, everything
  from a business catalog; controls stored as data; definition, SQL translation and execution separated; the AI decoupled
  from the controls engine. I reviewed and approved the detailed design.
- **Ready for a real AI model.** I planned Stage B as infrastructure for any model, as the assignment asks: the AI runs
  in its own service behind a provider interface; it receives the business meaning of the data, not table names; it
  must answer in a defined JSON contract; and every answer is validated against the catalog and translated into a
  regular Stage A control, shown for approval before it is saved or run. Replacing the Mock with a real model is one new
  provider class and a setting - the controls engine, the API and the UI do not change
  ([AI-STAGE-B.md §5](AI-STAGE-B.md#5-replacing-the-mock-with-a-real-model)).
- **The optional task.** I also designed the advanced capability: the AI analyzes the data model and proposes
  data-quality controls on its own - a safe profile of aggregates only (never rows), candidate controls from the model
  and from rules, each validated and previewed with its violation count, and the user approves
  ([AI-STAGE-B.md §6](AI-STAGE-B.md#6-design--the-ai-proposes-controls-on-its-own-optional-task-design-only)).
- **Decisions on open questions.** For Stage B I asked the reviewer, then decided on the interpretation myself: a real
  model understands the request, the Mock demonstrates receiving its answer, and the examples act as templates rather
  than synonym lists - because synonyms are not generic ([AI-STAGE-B.md](AI-STAGE-B.md)).
- **UX.** I used the system as a business user and directed the changes: only relevant fields in the field list, natural
  Hebrew names, no error shown before the user has typed, layout fixes.
- **Review and quality.** I reviewed the agent's work like a code review and sent it back when needed: root causes, not
  patches; every test suite green; a live check against the real database; an honest report of what was not verified.

**How I set up the agent**

- A project rules file (`CLAUDE.md`) with the working method and engineering standards, applied in every session.
- Persistent memory, so a decision was made once and kept.
- Approval gates: nothing installed, deleted or published without my approval.
- Verification loops: tests, mutation checks (breaking code on purpose to prove a test catches it), and end-to-end runs
  in the database, the API and the browser, with evidence shown to me before I accepted a change.

The agent wrote most of the code, tests and documents; I can explain every part and every decision.

**Within the product:** no AI service is called - Stage B uses a Mock, as the assignment requires.

---

<div dir="rtl">

## בעברית בקצרה

בניתי את הפרויקט בעבודה אג'נטית. **Claude Code** (מודל Claude Opus) עבד בתוך מאגר הקוד כסוכן אוטונומי: כתב קוד, הריץ בנייה, בדיקות, סקריפטים של מסד הנתונים ודפדפן. אני עבדתי מולו כמו ראש צוות מול מפתח:

- **פירקתי את הדרישות למשימות וקבעתי סדרי עדיפויות.** משימה אחת בכל פעם, שמסתיימת בדוח אימות ובשמירה.
- **קבעתי את עקרונות הארכיטקטורה:**
  - גנריות מלאה מתוך קטלוג עסקי;
  - בקרות שנשמרות כנתונים;
  - הפרדה בין ההגדרה, התרגום לשאילתה וההרצה;
  - בינה מלאכותית שמנותקת ממנוע הבקרות.
- **תכננתי את שלב ב' כתשתית מוכנה למודל בינה מלאכותית אמיתי,** כפי שהמטלה דורשת:
  - הבינה המלאכותית בשירות נפרד, מאחורי ממשק ספק אחיד;
  - היא מקבלת את המשמעות העסקית של הנתונים;
  - היא עונה במבנה מוגדר;
  - כל תשובה נבדקת מול הקטלוג ומתורגמת לבקרה רגילה שמוצגת לאישור.
  - החלפת המודל המדומה במודל אמיתי דורשת מחלקה אחת והגדרה, בלי לשנות את לוגיקת הבקרות.
- **ביצעתי גם את משימת הרשות:** תכנון יכולת שבה הבינה המלאכותית מנתחת את מבנה הנתונים ומציעה בקרות איכות באופן יזום:
  - פרופיל בטוח של סיכומים בלבד, בלי רשומות;
  - הצעות שנבדקות ומוצגות עם מספר החריגות;
  - אישור של המשתמש.
- **הכרעתי בשאלות פתוחות,** כמו הפרשנות לשלב ב'.
- **הכוונתי את חוויית המשתמש** אחרי שעבדתי עם המערכת כמשתמשת עסקית.
- **סקרתי כל תוצר כמו סקירת קוד:**
  - תיקון של שורש הבעיה, לא טלאי;
  - כל הבדיקות עוברות;
  - בדיקה חיה מול מסד הנתונים.

**הגדרתי לסוכן דרך עבודה:**
- קובץ כללים קבוע;
- זיכרון בין שיחות;
- נקודות אישור;
- לולאות אימות.

הסוכן כתב את רוב הקוד, ואני יכולה להסביר כל חלק וכל החלטה. המערכת עצמה אינה פונה לשירות בינה מלאכותית: שלב ב' מודגם באמצעות מודל מדומה.

</div>

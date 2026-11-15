/* =====================================================================
   05 - Sample business data: a valid baseline + planted violations.

   Deterministic: fixed Ids, no RAND(); every value is derived from the row number.
   Re-runnable: existing biz data is deleted and re-inserted.

   Baseline rules (so it never matches a sample control by accident):
     - department heads (Id 1-7) earn 32,000+; everybody else earns less than their manager
     - everybody in the baseline is an adult; HireDate >= BirthDate + 21 years
     - active employees: email + phone + an ACTIVE department, no TerminationDate
     - inactive employees: TerminationDate between HireDate and today
     - projects: EndDate NULL or >= StartDate

   Planted violations - each planted row violates exactly ONE sample control
   (documented in docs/SAMPLE-CONTROLS.md; checked by 99_verify.sql):
     #1 under 18 AND salary > 15,000 ............................ 3  (Ids 201-203; 201-202 active)
     #2 termination date earlier than hire date ................. 4  (Ids 204-207)
     #3 active employee in an inactive department ............... 5  (Ids 208-212)
     #4 active employee missing email / phone / department ...... 6  (Ids 213-218, incl. blank strings)
     #5 employee earns more than their own manager .............. 3  (Ids 220-222, manager 219)
     #6 project end date earlier than start date ................ 2  (Project Ids 19-20)
     #7 active department without any active employee ........... 1  (Department 8)

   Minors' birth dates are relative to the seed date, so they stay under 18
   for at least ~1.9 years after setup (the counts do not drift).
   ===================================================================== */
USE DataControlsDb;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

DECLARE @today DATE = CAST(GETDATE() AS DATE);

/* ---------- reset ---------- */
DELETE FROM biz.Projects;
DELETE FROM biz.Employees;      -- single statement: the self-reference (ManagerId) is checked at statement end
DELETE FROM biz.Departments;

/* ---------- departments (8 active + 2 inactive) ---------- */
SET IDENTITY_INSERT biz.Departments ON;
INSERT biz.Departments (Id, Name, Code, IsActive) VALUES
    (1,  N'משאבי אנוש',         N'HR',    1),
    (2,  N'כספים',              N'FIN',   1),
    (3,  N'פיתוח תוכנה',        N'DEV',   1),
    (4,  N'מכירות',             N'SALES', 1),
    (5,  N'שיווק',              N'MKT',   1),
    (6,  N'תפעול',              N'OPS',   1),
    (7,  N'שירות לקוחות',       N'CS',    1),
    (8,  N'רכש',                N'PROC',  1),   -- #7: active, but only inactive employees
    (9,  N'ארכיון',             N'ARCH',  0),
    (10, N'פרויקטים מיוחדים',  N'SPEC',  0);
SET IDENTITY_INSERT biz.Departments OFF;

/* ---------- name pools (20 x 20) ---------- */
DECLARE @first TABLE (i INT PRIMARY KEY, n NVARCHAR(50));
INSERT @first VALUES (0,N'יוסי'),(1,N'מיכל'),(2,N'דוד'),(3,N'נועה'),(4,N'אבי'),(5,N'שירה'),(6,N'משה'),(7,N'רחל'),
    (8,N'איתי'),(9,N'תמר'),(10,N'אורי'),(11,N'יעל'),(12,N'רון'),(13,N'הדס'),(14,N'עומר'),(15,N'ליאת'),
    (16,N'גיל'),(17,N'מאיה'),(18,N'עידו'),(19,N'אסתר');
DECLARE @last TABLE (i INT PRIMARY KEY, n NVARCHAR(50));
INSERT @last VALUES (0,N'כהן'),(1,N'לוי'),(2,N'מזרחי'),(3,N'פרץ'),(4,N'ביטון'),(5,N'אברהם'),(6,N'פרידמן'),(7,N'דהן'),
    (8,N'אזולאי'),(9,N'שפירא'),(10,N'גבאי'),(11,N'אוחיון'),(12,N'יוסף'),(13,N'חדד'),(14,N'עמר'),(15,N'קליין'),
    (16,N'גולן'),(17,N'וקנין'),(18,N'רוזן'),(19,N'שמעוני');

/* ---------- baseline employees (Id 1-200), all valid ---------- */
DECLARE @emp TABLE
(
    Id INT PRIMARY KEY, DepartmentId INT NULL, ManagerId INT NULL, IsActive BIT,
    BirthDate DATE, HireDate DATE, Salary DECIMAL(10,2)
);

-- 1-7: department heads (active, top of the hierarchy)
INSERT @emp (Id, DepartmentId, ManagerId, IsActive, BirthDate, HireDate, Salary)
SELECT s.value, s.value, NULL, 1,
       DATEADD(DAY, s.value * 211, '1968-01-01'),
       DATEADD(DAY, s.value * 97, '2008-01-01'),
       32000 + s.value * 1000
FROM GENERATE_SERIES(1, 7) AS s;

-- 8-187: staff of departments 1-7; every 10th is inactive (left the company)
INSERT @emp (Id, DepartmentId, ManagerId, IsActive, BirthDate, HireDate, Salary)
SELECT s.value, d.dept, d.dept,
       IIF(s.value % 10 = 0, 0, 1),
       b.birth,
       GREATEST(DATEADD(DAY, (s.value * 97) % 5400, '2010-01-01'), DATEADD(YEAR, 21, b.birth)),
       8000 + (s.value * 37) % 12000                                   -- 8,000 - 19,999 (< head 33,000+)
FROM GENERATE_SERIES(8, 187) AS s
CROSS APPLY (SELECT 1 + s.value % 7 AS dept) AS d
CROSS APPLY (SELECT DATEADD(DAY, (s.value * 131) % 11000, '1965-01-01') AS birth) AS b;   -- born 1965-1995

-- 188-189: department 8 (Procurement) - inactive employees only (#7)
-- 190-200: former employees of the inactive departments 9-10
INSERT @emp (Id, DepartmentId, ManagerId, IsActive, BirthDate, HireDate, Salary)
SELECT s.value, IIF(s.value <= 189, 8, 9 + s.value % 2), NULL, 0,
       DATEADD(DAY, s.value * 41, '1970-01-01'),
       DATEADD(DAY, s.value * 13, '2012-01-01'),
       9000 + s.value * 10
FROM GENERATE_SERIES(188, 200) AS s;

/* ---------- planted violations (Id 201-222) ---------- */
INSERT @emp (Id, DepartmentId, ManagerId, IsActive, BirthDate, HireDate, Salary) VALUES
    -- #1 minors earning > 15,000 (age 15-16 now; 203 is inactive)
    (201, 2, 2, 1, DATEADD(DAY, -30, DATEADD(YEAR, -16, @today)), DATEADD(DAY, -200, @today), 22000),
    (202, 3, 3, 1, DATEADD(DAY, -60, DATEADD(YEAR, -15, @today)), DATEADD(DAY, -150, @today), 16500),
    (203, 4, 4, 0, DATEADD(DAY, -90, DATEADD(YEAR, -16, @today)), DATEADD(DAY, -300, @today), 18000),
    -- #2 termination before hire (inactive)
    (204, 1, 1, 0, '1980-04-12', '2021-03-05', 9500),
    (205, 2, 2, 0, '1985-09-30', '2019-06-01', 11000),
    (206, 5, 5, 0, '1990-01-15', '2022-11-20', 10200),
    (207, 6, 6, 0, '1978-07-07', '2018-02-14', 12500),
    -- #3 active employees in inactive departments
    (208, 9,  NULL, 1, '1983-03-03', '2015-05-05', 11000),
    (209, 9,  NULL, 1, '1987-08-21', '2016-09-12', 10500),
    (210, 9,  NULL, 1, '1991-12-01', '2018-01-08', 9800),
    (211, 10, NULL, 1, '1979-05-19', '2014-03-23', 12800),
    (212, 10, NULL, 1, '1993-10-10', '2020-07-01', 9300),
    -- #4 active employees missing business-required data
    (213, 1, 1,    1, '1986-02-02', '2017-04-04', 9100),   -- email NULL
    (214, 2, 2,    1, '1988-06-16', '2019-08-18', 9200),   -- email ''
    (215, 3, 3,    1, '1984-11-11', '2016-12-12', 9300),   -- phone NULL
    (216, 4, 4,    1, '1992-03-27', '2021-01-31', 9400),   -- phone '   '
    (217, NULL, NULL, 1, '1989-09-09', '2020-10-10', 9500), -- no department
    (218, NULL, NULL, 1, '1995-01-25', '2022-02-22', 9600), -- no department
    -- #5 team lead 219 (valid) and three reports who earn more than him
    (219, 3, 3,   1, '1982-12-24', '2015-02-01', 12000),
    (220, 3, 219, 1, '1990-05-05', '2019-03-15', 13000),
    (221, 3, 219, 1, '1991-07-17', '2020-06-20', 14800),
    (222, 3, 219, 1, '1994-02-28', '2021-09-09', 12500);

/* ---------- materialize employees ---------- */
SET IDENTITY_INSERT biz.Employees ON;
INSERT biz.Employees
    (Id, EmployeeNumber, FirstName, LastName, BirthDate, Email, Phone, HireDate, TerminationDate,
     Salary, IsActive, DepartmentId, ManagerId)
SELECT e.Id,
       CONCAT(N'E', FORMAT(10000 + e.Id, '00000')),
       f.n,
       l.n,
       e.BirthDate,
       CASE e.Id WHEN 213 THEN NULL WHEN 214 THEN N'' ELSE CONCAT(N'emp', e.Id, N'@company.co.il') END,
       CASE e.Id WHEN 215 THEN NULL WHEN 216 THEN N'   '
            ELSE CONCAT(N'05', e.Id % 10, N'-', FORMAT(1000000 + (e.Id * 7919) % 9000000, '0000000')) END,
       e.HireDate,
       CASE
           WHEN e.Id = 204 THEN '2020-01-01'               -- #2: before hire
           WHEN e.Id = 205 THEN '2019-01-31'
           WHEN e.Id = 206 THEN '2022-06-30'
           WHEN e.Id = 207 THEN '2017-12-31'
           WHEN e.IsActive = 0 THEN LEAST(DATEADD(DAY, 200 + (e.Id * 53) % 1500, e.HireDate), DATEADD(DAY, -30, @today))
           ELSE NULL
       END,
       e.Salary,
       e.IsActive,
       e.DepartmentId,
       NULL                                                 -- managers set below (self-reference)
FROM @emp AS e
JOIN @first AS f ON f.i = e.Id % 20
JOIN @last  AS l ON l.i = (e.Id / 20 + e.Id * 3) % 20;   -- independent of the first-name index -> distinct full names
SET IDENTITY_INSERT biz.Employees OFF;

UPDATE x SET ManagerId = e.ManagerId
FROM biz.Employees AS x JOIN @emp AS e ON e.Id = x.Id
WHERE e.ManagerId IS NOT NULL;

/* ---------- projects (18 valid + 2 planted) ---------- */
SET IDENTITY_INSERT biz.Projects ON;
INSERT biz.Projects (Id, Name, DepartmentId, StartDate, EndDate, Budget, Status)
SELECT s.value,
       CONCAT(N'פרויקט ', p.name),
       1 + s.value % 8,
       st.start,
       CASE s.value % 4 WHEN 2 THEN DATEADD(DAY, 90 + s.value * 11, st.start)      -- Completed
                        WHEN 3 THEN DATEADD(DAY, 30 + s.value * 5, st.start)       -- Cancelled
                        ELSE NULL END,                                             -- Planned / Active
       100000 + s.value * 25000,
       CASE s.value % 4 WHEN 0 THEN N'Planned' WHEN 1 THEN N'Active' WHEN 2 THEN N'Completed' ELSE N'Cancelled' END
FROM GENERATE_SERIES(1, 18) AS s
CROSS APPLY (SELECT DATEADD(DAY, s.value * 45, '2022-01-01') AS start) AS st
CROSS APPLY (SELECT CHOOSE(1 + s.value % 9, N'אלפא', N'בטא', N'גמא', N'דלתא', N'אומגה', N'אורן', N'ברוש', N'תמר', N'זית')
                    + N' ' + CAST(s.value AS NVARCHAR(3)) AS name) AS p;

INSERT biz.Projects (Id, Name, DepartmentId, StartDate, EndDate, Budget, Status) VALUES
    (19, N'פרויקט מעבר לענן',      3, '2024-05-01', '2024-03-15', 450000, N'Completed'),   -- #6
    (20, N'פרויקט שדרוג מערכות', 6, '2023-09-10', '2023-09-01', 120000, N'Completed');   -- #6
SET IDENTITY_INSERT biz.Projects OFF;

COMMIT TRANSACTION;

SELECT CONCAT('departments=', (SELECT COUNT(*) FROM biz.Departments),
              ', employees=', (SELECT COUNT(*) FROM biz.Employees),
              ', projects=', (SELECT COUNT(*) FROM biz.Projects)) AS seeded;
GO

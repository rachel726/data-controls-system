/* =====================================================================
   01 - Schema "biz": sample business data that controls run against.

   IMPORTANT - no business CHECK constraints on purpose:
   rules such as "termination date >= hire date" or "salary > 0" are NOT
   enforced here. Real-world databases contain such bad data; detecting it
   is exactly the job of the controls system. Only structural integrity
   (PK, FK, NOT NULL for identity columns, unique business keys) is enforced.

   Idempotent: objects are created only if missing.
   ===================================================================== */
USE DataControlsDb;
GO

IF SCHEMA_ID(N'biz') IS NULL
    EXEC (N'CREATE SCHEMA biz AUTHORIZATION dbo;');
GO

/* ---------- Departments ---------- */
IF OBJECT_ID(N'biz.Departments', N'U') IS NULL
CREATE TABLE biz.Departments
(
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    Name      NVARCHAR(100)     NOT NULL,
    Code      NVARCHAR(20)      NOT NULL CONSTRAINT UQ_Departments_Code UNIQUE,
    IsActive  BIT               NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT (1)
);
GO

/* ---------- Employees ---------- */
IF OBJECT_ID(N'biz.Employees', N'U') IS NULL
CREATE TABLE biz.Employees
(
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    EmployeeNumber  NVARCHAR(20)      NOT NULL CONSTRAINT UQ_Employees_EmployeeNumber UNIQUE,
    FirstName       NVARCHAR(50)      NOT NULL,
    LastName        NVARCHAR(50)      NOT NULL,
    BirthDate       DATE              NULL,
    Email           NVARCHAR(200)     NULL,
    Phone           NVARCHAR(30)      NULL,
    HireDate        DATE              NOT NULL,
    TerminationDate DATE              NULL,
    Salary          DECIMAL(10,2)     NULL,
    IsActive        BIT               NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1),
    DepartmentId    INT               NULL
        CONSTRAINT FK_Employees_Departments REFERENCES biz.Departments (Id),
    ManagerId       INT               NULL
        CONSTRAINT FK_Employees_Manager REFERENCES biz.Employees (Id)
);
GO

/* ---------- Projects ---------- */
IF OBJECT_ID(N'biz.Projects', N'U') IS NULL
CREATE TABLE biz.Projects
(
    Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
    Name          NVARCHAR(150)     NOT NULL,
    DepartmentId  INT               NOT NULL
        CONSTRAINT FK_Projects_Departments REFERENCES biz.Departments (Id),
    StartDate     DATE              NOT NULL,
    EndDate       DATE              NULL,
    Budget        DECIMAL(14,2)     NULL,
    Status        NVARCHAR(20)      NOT NULL  -- Planned | Active | Completed | Cancelled (catalog lists the values)
);
GO

/* ---------- Indexes ----------
   SQL Server does not index foreign keys automatically; every FK used for joins gets one.
   - Employees(DepartmentId) INCLUDE (IsActive): serves both the employee->department join and
     the department->employees EXISTS check ("department without active employees") without key lookups.
   - Employees(ManagerId): self-join employee->manager.
   - Employees(BirthDate): "age" filters are rewritten to sargable BirthDate ranges.
   No standalone index on IsActive: a bit column is too unselective to be useful on its own. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Employees_DepartmentId' AND object_id = OBJECT_ID(N'biz.Employees'))
    CREATE INDEX IX_Employees_DepartmentId ON biz.Employees (DepartmentId) INCLUDE (IsActive);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Employees_ManagerId' AND object_id = OBJECT_ID(N'biz.Employees'))
    CREATE INDEX IX_Employees_ManagerId ON biz.Employees (ManagerId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Employees_BirthDate' AND object_id = OBJECT_ID(N'biz.Employees'))
    CREATE INDEX IX_Employees_BirthDate ON biz.Employees (BirthDate);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Projects_DepartmentId' AND object_id = OBJECT_ID(N'biz.Projects'))
    CREATE INDEX IX_Projects_DepartmentId ON biz.Projects (DepartmentId);
GO

PRINT 'Schema biz ready.';
GO

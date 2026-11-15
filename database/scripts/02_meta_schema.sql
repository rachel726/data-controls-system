/* =====================================================================
   02 - Schema "meta": the semantic catalog (business dictionary) and the
   saved control definitions.

   The catalog is what makes the engine generic: it maps business terms
   ("עובד", "גיל", "מחלקה") to physical tables/columns/relations. No table or
   column name appears in application code - only here and in the seed data.

   Unlike "biz", integrity rules ARE enforced here: this is the system's own
   configuration, and a broken catalog would break every control.
   Idempotent: objects are created only if missing.
   ===================================================================== */
USE DataControlsDb;
GO
-- Required for the filtered unique index below (sqlcmd defaults to OFF).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF SCHEMA_ID(N'meta') IS NULL
    EXEC (N'CREATE SCHEMA meta AUTHORIZATION dbo;');
GO

/* ---------- Entities: business objects (e.g. employee = biz.Employees) ---------- */
IF OBJECT_ID(N'meta.Entities', N'U') IS NULL
CREATE TABLE meta.Entities
(
    Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Entities PRIMARY KEY,
    [Key]             VARCHAR(50)       NOT NULL CONSTRAINT UQ_Entities_Key UNIQUE,
    DisplayName       NVARCHAR(100)     NOT NULL,   -- עובד
    DisplayNamePlural NVARCHAR(100)     NOT NULL,   -- עובדים
    Gender            VARCHAR(10)       NOT NULL CONSTRAINT DF_Entities_Gender DEFAULT ('Masculine'),  -- Hebrew grammar: "בהם" / "בהן"
    Description       NVARCHAR(500)     NULL,
    SchemaName        SYSNAME           NOT NULL,
    TableName         SYSNAME           NOT NULL,
    PrimaryKeyColumn  SYSNAME           NOT NULL,
    DisplayFieldKey   VARCHAR(50)       NOT NULL,   -- field shown as the record title in results
    SortOrder         INT               NOT NULL CONSTRAINT DF_Entities_SortOrder DEFAULT (0),
    -- keys are used in dot-separated field paths ("department.isActive"): letters/digits only, starting with a letter
    CONSTRAINT CK_Entities_Key CHECK ([Key] COLLATE Latin1_General_BIN LIKE '[a-z]%' AND [Key] COLLATE Latin1_General_BIN NOT LIKE '%[^a-zA-Z0-9]%'),
    CONSTRAINT CK_Entities_Gender CHECK (Gender IN ('Masculine', 'Feminine')),
    CONSTRAINT UQ_Entities_Table UNIQUE (SchemaName, TableName)
);
GO

/* ---------- Fields: business attributes of an entity ---------- */
IF OBJECT_ID(N'meta.Fields', N'U') IS NULL
CREATE TABLE meta.Fields
(
    Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Fields PRIMARY KEY,
    EntityId             INT               NOT NULL CONSTRAINT FK_Fields_Entities REFERENCES meta.Entities (Id),
    [Key]                VARCHAR(50)       NOT NULL,   -- age
    DisplayName          NVARCHAR(100)     NOT NULL,   -- גיל
    Description          NVARCHAR(500)     NULL,
    SourceColumn         SYSNAME           NOT NULL,   -- BirthDate
    DataType             VARCHAR(20)       NOT NULL,
    Transform            VARCHAR(20)       NOT NULL CONSTRAINT DF_Fields_Transform DEFAULT ('None'),
    TransformArg         SYSNAME           NULL,       -- FullName: the last-name column
    IsRequiredByBusiness BIT               NOT NULL CONSTRAINT DF_Fields_IsRequired DEFAULT (0),
    IsVisibleInResults   BIT               NOT NULL CONSTRAINT DF_Fields_IsVisible DEFAULT (1),
    IsSearchable         BIT               NOT NULL CONSTRAINT DF_Fields_IsSearchable DEFAULT (0),
    AllowedValuesJson    NVARCHAR(MAX)     NULL,       -- [{"value":"Active","label":"פעיל"}]
    Unit                 NVARCHAR(10)      NULL,       -- ₪
    TrueText             NVARCHAR(100)     NULL,       -- Boolean wording: "פעיל" -> "עובד פעיל"
    FalseText            NVARCHAR(100)     NULL,       -- "אינו פעיל" -> "עובד אינו פעיל"
    SortOrder            INT               NOT NULL CONSTRAINT DF_Fields_SortOrder DEFAULT (0),
    CONSTRAINT UQ_Fields_EntityKey UNIQUE (EntityId, [Key]),
    CONSTRAINT CK_Fields_Key CHECK ([Key] COLLATE Latin1_General_BIN LIKE '[a-z]%' AND [Key] COLLATE Latin1_General_BIN NOT LIKE '%[^a-zA-Z0-9]%'),
    CONSTRAINT CK_Fields_DataType CHECK (DataType IN ('Text', 'Integer', 'Decimal', 'Date', 'Boolean', 'Enum')),
    CONSTRAINT CK_Fields_Transform CHECK (Transform IN ('None', 'YearsSince', 'FullName')),
    CONSTRAINT CK_Fields_TransformArg CHECK (Transform <> 'FullName' OR TransformArg IS NOT NULL),
    CONSTRAINT CK_Fields_AllowedValues CHECK (AllowedValuesJson IS NULL OR ISJSON(AllowedValuesJson, ARRAY) = 1),
    CONSTRAINT CK_Fields_EnumHasValues CHECK (DataType <> 'Enum' OR AllowedValuesJson IS NOT NULL),
    -- wording of a yes/no field: both or neither, and only for Boolean fields
    CONSTRAINT CK_Fields_BooleanTexts CHECK ((TrueText IS NULL AND FalseText IS NULL)
                                          OR (DataType = 'Boolean' AND TrueText IS NOT NULL AND FalseText IS NOT NULL))
);
GO

/* ---------- Relations: how entities are linked (each FK is registered in both directions) ---------- */
IF OBJECT_ID(N'meta.Relations', N'U') IS NULL
CREATE TABLE meta.Relations
(
    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Relations PRIMARY KEY,
    [Key]        VARCHAR(50)       NOT NULL,   -- department
    FromEntityId INT               NOT NULL CONSTRAINT FK_Relations_From REFERENCES meta.Entities (Id),
    ToEntityId   INT               NOT NULL CONSTRAINT FK_Relations_To REFERENCES meta.Entities (Id),
    FromColumn   SYSNAME           NOT NULL,
    ToColumn     SYSNAME           NOT NULL,
    Cardinality  VARCHAR(20)       NOT NULL,
    DisplayName  NVARCHAR(100)     NOT NULL,   -- מחלקה
    Description  NVARCHAR(500)     NULL,
    CONSTRAINT UQ_Relations_FromKey UNIQUE (FromEntityId, [Key]),
    CONSTRAINT CK_Relations_Key CHECK ([Key] COLLATE Latin1_General_BIN LIKE '[a-z]%' AND [Key] COLLATE Latin1_General_BIN NOT LIKE '%[^a-zA-Z0-9]%'),
    CONSTRAINT CK_Relations_Cardinality CHECK (Cardinality IN ('ManyToOne', 'OneToMany'))
);
GO

/* ---------- Controls: saved control definitions ----------
   The definition is stored as JSON (a condition tree in business terms), never as SQL.
   Last-run columns let the list screen show the violation count and record failed runs. */
IF OBJECT_ID(N'meta.Controls', N'U') IS NULL
CREATE TABLE meta.Controls
(
    Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Controls PRIMARY KEY,
    Name               NVARCHAR(200)     NOT NULL,
    Description        NVARCHAR(1000)    NULL,
    RootEntityKey      VARCHAR(50)       NOT NULL
        CONSTRAINT FK_Controls_RootEntity REFERENCES meta.Entities ([Key]),
    DefinitionJson     NVARCHAR(MAX)     NOT NULL,
    IsActive           BIT               NOT NULL CONSTRAINT DF_Controls_IsActive DEFAULT (1),
    CreatedAt          DATETIME2(0)      NOT NULL CONSTRAINT DF_Controls_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt          DATETIME2(0)      NOT NULL CONSTRAINT DF_Controls_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    LastRunAt          DATETIME2(0)      NULL,
    LastRunStatus      VARCHAR(20)       NULL,
    LastViolationCount INT               NULL,
    LastRunError       NVARCHAR(1000)    NULL,
    CONSTRAINT CK_Controls_Definition CHECK (ISJSON(DefinitionJson, OBJECT) = 1),
    CONSTRAINT CK_Controls_LastRunStatus CHECK (LastRunStatus IS NULL OR LastRunStatus IN ('Succeeded', 'Failed'))
);
GO

/* Name is unique among active controls only, so a deleted (soft) control's name can be reused. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Controls_Name_Active' AND object_id = OBJECT_ID(N'meta.Controls'))
    CREATE UNIQUE INDEX UX_Controls_Name_Active ON meta.Controls (Name) WHERE IsActive = 1;
GO

PRINT 'Schema meta ready.';
GO

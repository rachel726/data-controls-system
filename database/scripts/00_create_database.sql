/* =====================================================================
   00 - Create the database (if missing).
   Dropping/recreating is done by setup.ps1 -Force, never by this script.
   Collation is explicit (Hebrew, case-insensitive) so sorting and LIKE
   behave the same on every server, regardless of the server default.
   ===================================================================== */
USE master;
GO

IF DB_ID(N'DataControlsDb') IS NULL
BEGIN
    CREATE DATABASE DataControlsDb COLLATE Hebrew_100_CI_AS;
    PRINT 'Database DataControlsDb created.';
END
ELSE
    PRINT 'Database DataControlsDb already exists - skipped.';
GO

ALTER DATABASE DataControlsDb SET RECOVERY SIMPLE;
GO

-- Reads see the last committed version instead of taking shared locks: reading the controls list while a control
-- is saved or deleted can then never deadlock (a writer locks the row, then the unique-name index; a reader the reverse).
IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'DataControlsDb') = 0
    ALTER DATABASE DataControlsDb SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO

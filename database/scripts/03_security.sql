/* =====================================================================
   03 - Security: a read-only database principal for running controls.

   Every generated control query is executed as:
       EXECUTE AS USER = 'dc_query_reader'; <SELECT ...>; REVERT;
   so even if the query engine had a bug, the database itself would refuse
   to modify data, change schema, or read the system's own "meta" tables.
   This is the last of several layers (catalog-whitelisted identifiers,
   parameterized values, SELECT-only renderer, limits & timeout).

   - USER WITHOUT LOGIN: nobody can connect as this user; it exists only to be impersonated.
   - The application's own principal needs IMPERSONATE on it. In development the app
     connects with Windows auth as a sysadmin/db_owner, which already has that right.
     Production: GRANT IMPERSONATE ON USER::dc_query_reader TO <app user>;
   Idempotent.
   ===================================================================== */
USE DataControlsDb;
GO

IF USER_ID(N'dc_query_reader') IS NULL
    CREATE USER dc_query_reader WITHOUT LOGIN WITH DEFAULT_SCHEMA = biz;
GO

-- Read access to business data only.
GRANT SELECT ON SCHEMA::biz TO dc_query_reader;

-- Explicit DENY (wins over any grant that may come from a role such as public):
-- no data changes and no schema changes in biz ...
DENY INSERT, UPDATE, DELETE, ALTER, EXECUTE, REFERENCES ON SCHEMA::biz TO dc_query_reader;
-- ... and no access at all to the system's own configuration.
DENY SELECT, INSERT, UPDATE, DELETE, ALTER, EXECUTE, REFERENCES ON SCHEMA::meta TO dc_query_reader;
-- No object creation anywhere in the database.
DENY CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE FUNCTION, CREATE SCHEMA TO dc_query_reader;
GO

PRINT 'Read-only user dc_query_reader ready.';
GO

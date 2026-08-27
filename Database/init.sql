/*
    Safety notice
    =============
    The original init.sql dropped the three production WMS tables. It has been
    retired so that running an old setup command can never erase warehouse
    data. Run Database/upgrade_infeed_v2.sql instead; it is idempotent and
    creates both the original WMS schema (when absent) and the v2 QR/FEFO
    tables without deleting existing records.
*/
PRINT N'No database changes were made by init.sql. Run upgrade_infeed_v2.sql.';

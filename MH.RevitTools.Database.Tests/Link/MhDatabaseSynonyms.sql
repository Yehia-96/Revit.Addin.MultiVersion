-- Test-only: in a throwaway database, point dbo's table names at dev MH_Database, so RevitViews.sql
-- runs unchanged over the real data. Synonyms only read through; nothing is written to MH_Database.
CREATE SYNONYM dbo.Projects FOR MH_Database.dbo.Projects;
CREATE SYNONYM dbo.Parameters FOR MH_Database.dbo.Parameters;
CREATE SYNONYM dbo.ParameterSets FOR MH_Database.dbo.ParameterSets;
CREATE SYNONYM dbo.ParameterGroups FOR MH_Database.dbo.ParameterGroups;
CREATE SYNONYM dbo.ProjectObjectParameters FOR MH_Database.dbo.ProjectObjectParameters;
CREATE SYNONYM dbo.Objects FOR MH_Database.dbo.Objects;
CREATE SYNONYM dbo.ComponentCategories FOR MH_Database.dbo.ComponentCategories;
CREATE SYNONYM dbo.Materials FOR MH_Database.dbo.Materials;
CREATE SYNONYM dbo.MaterialCategories FOR MH_Database.dbo.MaterialCategories;
CREATE SYNONYM dbo.MaterialGroups FOR MH_Database.dbo.MaterialGroups;
CREATE SYNONYM dbo.RevitAppearances FOR MH_Database.dbo.RevitAppearances;
CREATE SYNONYM dbo.MaterialParameterValues FOR MH_Database.dbo.MaterialParameterValues;
CREATE SYNONYM dbo.AuditLogs FOR MH_Database.dbo.AuditLogs;

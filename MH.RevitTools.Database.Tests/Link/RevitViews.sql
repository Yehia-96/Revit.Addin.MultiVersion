-- The revit views that the Revit add-in (MH.RevitTools, database link) reads: the add-in tests' copy.
--
-- Since 2026-11-19 the views live in Studio's migration AddRevitLink (ProjectsApp, Database.Data,
-- AddRevitLink.ViewBatches), which is their one source. This copy lets the add-in's tests build the
-- same views on stand-in tables with known rows (Link/StandInTables.sql). When the migration changes a
-- view, change it here the same way. The tests over dev MH_Database read the real views instead.
--
-- The add-in reads exactly the columns below, by name; extra columns are ignored. The views:
--   - build Bauteil-IDs and Material-IDs the way Studio's BauteilIds.Format and MaterialIds.Format do;
--   - take the change stamp and "who changed it, when" from AuditLogs, never its Changes column;
--   - map the dictionary's RevitCategory to Revit's BuiltInCategory names.
-- It's safe to run again: the schema is created only if missing, and CREATE OR ALTER replaces a view.
-- Schema revit is owned by dbo, so the views read dbo's tables while the add-in's role only gets rights
-- on schema revit.
IF SCHEMA_ID(N'revit') IS NULL
    EXEC (N'CREATE SCHEMA revit AUTHORIZATION dbo');
GO
CREATE OR ALTER VIEW revit.Projects AS
SELECT ProjectId, ProjectNumber, ProjectSubNumber, Name
FROM dbo.Projects;
GO
-- The dictionary's parameters that go to Revit: those with a RevitCategory. Guid is the parameter's
-- GUID in the master shared parameter file. RevitName is the name Revit shows, which Studio settles
-- (suffixed where plain names clash on one category); until it has, the plain name.
CREATE OR ALTER VIEW revit.Parameters AS
SELECT p.ParameterId,
       p.SharedParameterGuid AS Guid,
       COALESCE(NULLIF(LTRIM(RTRIM(p.RevitName)), N''), p.Name) AS RevitName,
       s.Code AS SetCode,
       g.Code AS GroupCode,
       p.Name,
       p.DataType,
       NULLIF(LTRIM(RTRIM(p.Unit)), N'') AS Unit,
       CASE WHEN LTRIM(RTRIM(p.RevitCategory)) = N'Material' THEN N'Material' ELSE N'Object' END AS Target
FROM dbo.Parameters p
LEFT JOIN dbo.ParameterSets s ON s.ParameterSetId = p.ParameterSetId
LEFT JOIN dbo.ParameterGroups g ON g.ParameterGroupId = p.ParameterGroupId
WHERE NULLIF(LTRIM(RTRIM(p.RevitCategory)), N'') IS NOT NULL;
GO
-- Revit's own category names, so nothing depends on Revit's language. One line per category the
-- dictionary uses; a parameter whose category isn't listed gets no row, and the add-in reports it.
CREATE OR ALTER VIEW revit.ParameterCategories AS
SELECT p.ParameterId, c.Category
FROM dbo.Parameters p
JOIN (VALUES (N'Allgemeines Modell', N'OST_GenericModel'),
             (N'Material', N'OST_Materials')) AS c (RevitCategory, Category)
    ON c.RevitCategory = LTRIM(RTRIM(p.RevitCategory));
GO
-- One value of one parameter on one object of a project, keyed by the object's Bauteil-ID: Studio's
-- BauteilIds.Format from the object's catalog entry (category code, Bauteilnummer in four digits,
-- Bauteilunternummer in two). Objects without all three are left out, as Studio shows no ID for them.
-- ChangedAtUtc and ChangedBy: the newest history row for that value.
CREATE OR ALTER VIEW revit.ObjectValues AS
SELECT v.ProjectId,
       id.BauteilId,
       v.ParameterId,
       v.Value,
       h.OccurredAtUtc AS ChangedAtUtc,
       h.UserDisplay AS ChangedBy
FROM dbo.ProjectObjectParameters v
JOIN dbo.Objects o ON o.ObjectEntityId = v.ObjectEntityId
LEFT JOIN dbo.ComponentCategories cc ON cc.ComponentCategoryId = o.ComponentCategoryId
CROSS APPLY (SELECT UPPER(NULLIF(LTRIM(RTRIM(cc.Code)), N'')) + N'-' + FORMAT(o.BauteilNummer, '0000')
                    + N'-' + FORMAT(o.BauteilUnterNummer, '00') AS BauteilId) id
JOIN revit.Parameters rp ON rp.ParameterId = v.ParameterId
OUTER APPLY (SELECT TOP (1) a.OccurredAtUtc, a.UserDisplay
             FROM dbo.AuditLogs a
             WHERE a.EntityName = N'ProjectObjectParameter'
               AND a.EntityKey = CONCAT(v.ProjectId, N'|', v.ObjectEntityId, N'|', v.ParameterId)
             ORDER BY a.AuditLogId DESC) h
WHERE id.BauteilId IS NOT NULL;
GO
-- A project's own materials, keyed by Studio's MaterialIds.Format: category code; the project's number
-- and sub-number (a global category's placeholders instead); group code; material number. E.g.
-- MB-1761-01-BET-0031. Global materials (the MP products) belong to no project and aren't listed.
-- MaterialKey only links MaterialValues below; the add-in ignores it.
CREATE OR ALTER VIEW revit.Materials AS
SELECT m.ProjectId,
       id.MaterialId,
       m.Name AS MaterialName,
       mg.Code AS MaterialGroupCode,
       mg.Name AS MaterialGroup,
       ra.SurfacePattern,
       ra.CutPattern,
       m.MaterialId AS MaterialKey
FROM dbo.Materials m
JOIN dbo.Projects pr ON pr.ProjectId = m.ProjectId
JOIN dbo.MaterialCategories mc ON mc.MaterialCategoryId = m.MaterialCategoryId
JOIN dbo.MaterialGroups mg ON mg.MaterialGroupId = m.MaterialGroupId
LEFT JOIN dbo.RevitAppearances ra ON ra.RevitAppearanceId = m.RevitAppearanceId
CROSS APPLY (SELECT UPPER(LTRIM(RTRIM(mc.Code)))
                    + N'-' + FORMAT(CASE WHEN mc.IsProjectSpecific = 1 THEN pr.ProjectNumber ELSE mc.CodeProjectNumber END, '0000')
                    + N'-' + FORMAT(CASE WHEN mc.IsProjectSpecific = 1 THEN pr.ProjectSubNumber ELSE mc.CodeProjectSubNumber END, '00')
                    + N'-' + UPPER(LTRIM(RTRIM(mg.Code))) + N'-' + FORMAT(m.MaterialNumber, '0000') AS MaterialId) id
WHERE id.MaterialId IS NOT NULL;
GO
-- One value of one parameter on one material of a project.
CREATE OR ALTER VIEW revit.MaterialValues AS
SELECT mat.ProjectId,
       mat.MaterialId,
       v.ParameterId,
       v.Value,
       h.OccurredAtUtc AS ChangedAtUtc,
       h.UserDisplay AS ChangedBy
FROM dbo.MaterialParameterValues v
JOIN revit.Materials mat ON mat.MaterialKey = v.MaterialId
JOIN revit.Parameters rp ON rp.ParameterId = v.ParameterId
OUTER APPLY (SELECT TOP (1) a.OccurredAtUtc, a.UserDisplay
             FROM dbo.AuditLogs a
             WHERE a.EntityName = N'MaterialParameterValue'
               AND a.EntityKey = CONCAT(v.MaterialId, N'|', v.ParameterId)
             ORDER BY a.AuditLogId DESC) h;
GO
-- The change stamp. Studio adds a history row with every change it saves, so the number of rows moves
-- with every committed change, whatever order transactions commit in; the newest id alone could miss
-- one that committed late. Logins and settings don't touch anything the link reads and are left out.
-- One stamp for all projects: it only tells the add-in when to read again.
CREATE OR ALTER VIEW revit.ProjectStamps AS
SELECT p.ProjectId,
       (SELECT CAST(COUNT_BIG(a.AuditLogId) * CAST(4294967296 AS bigint)
                    + ISNULL(MAX(a.AuditLogId), 0) % CAST(4294967296 AS bigint) AS bigint)
        FROM dbo.AuditLogs a
        WHERE a.EntityName NOT IN (N'User', N'AppSetting')) AS Stamp
FROM dbo.Projects p;
GO

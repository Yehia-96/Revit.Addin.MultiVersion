-- A stand-in for the MH_Database tables the add-in reads: the columns it uses, with the real types,
-- and a few known rows. Tests the add-in's queries without touching the real database.
CREATE TABLE dbo.Projects (
    ProjectId int NOT NULL PRIMARY KEY, ProjectNumber int NOT NULL, ProjectSubNumber int NOT NULL, Name nvarchar(450) NOT NULL);
CREATE TABLE dbo.ComponentCategories (
    ComponentCategoryId int NOT NULL PRIMARY KEY, Code nvarchar(10) NOT NULL, Name nvarchar(100) NOT NULL);
CREATE TABLE dbo.Objects (
    ObjectEntityId int NOT NULL PRIMARY KEY, Name nvarchar(450) NOT NULL, ComponentCategoryId int NULL,
    BauteilNummer int NULL, BauteilUnterNummer int NULL);
CREATE TABLE dbo.ParameterSets (ParameterSetId int NOT NULL PRIMARY KEY, Code nvarchar(3) NOT NULL);
CREATE TABLE dbo.ParameterGroups (ParameterGroupId int NOT NULL PRIMARY KEY, Code nvarchar(3) NOT NULL);
CREATE TABLE dbo.Parameters (
    ParameterId int NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL, DataType nvarchar(max) NOT NULL,
    Unit nvarchar(max) NOT NULL, ParameterSetId int NULL, ParameterGroupId int NULL, RevitCategory nvarchar(50) NULL,
    SharedParameterGuid uniqueidentifier NOT NULL, RevitName nvarchar(250) NULL);
CREATE TABLE dbo.ProjectObjectParameters (
    ProjectId int NOT NULL, ObjectEntityId int NOT NULL, ParameterId int NOT NULL, Value nvarchar(2000) NOT NULL);
CREATE TABLE dbo.MaterialCategories (
    MaterialCategoryId int NOT NULL PRIMARY KEY, Code nvarchar(10) NOT NULL, Name nvarchar(100) NOT NULL,
    IsProjectSpecific bit NOT NULL, CodeProjectNumber int NULL, CodeProjectSubNumber int NULL);
CREATE TABLE dbo.MaterialGroups (MaterialGroupId int NOT NULL PRIMARY KEY, Code nvarchar(3) NOT NULL, Name nvarchar(100) NOT NULL);
CREATE TABLE dbo.RevitAppearances (
    RevitAppearanceId int NOT NULL PRIMARY KEY, SurfacePattern nvarchar(100) NULL, CutPattern nvarchar(100) NULL);
CREATE TABLE dbo.Materials (
    MaterialId int NOT NULL PRIMARY KEY, Name nvarchar(200) NOT NULL, ProjectId int NULL, MaterialCategoryId int NULL,
    MaterialGroupId int NULL, MaterialNumber int NULL, RevitAppearanceId int NULL);
CREATE TABLE dbo.MaterialParameterValues (
    MaterialId int NOT NULL, ParameterId int NOT NULL, ProjectId int NULL, Value nvarchar(max) NULL);
CREATE TABLE dbo.AuditLogs (
    AuditLogId bigint NOT NULL PRIMARY KEY, OccurredAtUtc datetime2 NOT NULL, UserDisplay nvarchar(200) NOT NULL,
    EntityName nvarchar(200) NOT NULL, EntityKey nvarchar(400) NOT NULL);
GO
INSERT dbo.Projects VALUES
    (13, 1761, 1, N'Ulm'),
    (1, 1760, 1, N'2. S-Bahn-Stammstrecke München');
INSERT dbo.ComponentCategories VALUES
    (2, N'BT', N'Bauteil'),
    (4, N'et', N'Einbauteil');
INSERT dbo.Objects VALUES
    (69, N'Bodenplatte', 2, 1101, 1),            -- BT-1101-01
    (392, N'Schlitzrinne links', 4, 2101, 12),   -- ET-2101-12: the code in capitals, two-digit Unternummer
    (290, N'Schlitzrinne', NULL, NULL, NULL);    -- no Bauteil-ID, so its values are left out
INSERT dbo.ParameterSets VALUES (1, N'BAU'), (2, N'MAT');
INSERT dbo.ParameterGroups VALUES (1, N'GEO'), (2, N'BES');
INSERT dbo.Parameters VALUES
    (61, N'Dicke', N'Double', N'm', 1, 1, N'Allgemeines Modell',
        '3f1c2a6e-8b4d-4c1e-9a7f-61d1c4e0a061', NULL),                     -- no Revit name yet: the plain name
    (140, N'Bezeichnung', N'String', N'', 2, 2, N'Material',
        '0c5e9b7a-2d3f-4a8b-b1c6-140d2e3f4a5b', N'Bezeichnung (Material)'), -- a suffixed Revit name
    (8, N'Wandstärke', N'Double', N'cm', 1, 1, N'Wand',                     -- a Revit category the views don't map yet
        '8a0b1c2d-3e4f-4a5b-8c6d-008e9f0a1b2c', N'  '),                     -- a blank Revit name counts as none
    (7, N'Interne Notiz', N'String', N'', 1, 2, NULL,                       -- not for Revit
        '7d6c5b4a-3f2e-4d1c-8b0a-007f6e5d4c3b', NULL);
INSERT dbo.ProjectObjectParameters VALUES
    (13, 69, 61, N'0.3'),
    (13, 392, 61, N'[Modeller]'),
    (13, 290, 61, N'0.5'),         -- object without a Bauteil-ID
    (13, 69, 7, N'nur intern'),    -- parameter not for Revit
    (1, 69, 61, N'1');             -- another project
INSERT dbo.MaterialCategories VALUES
    (1, N'MB', N'Bauteil', 1, NULL, NULL),
    (4, N'MP', N'Produkt', 0, 9999, 99);
INSERT dbo.MaterialGroups VALUES (1, N'BET', N'Beton');
INSERT dbo.RevitAppearances VALUES (1, NULL, N'Ortbeton - bewehrt');
INSERT dbo.Materials VALUES
    (123, N'31-Beton, C35/45', 13, 1, 1, 31, 1),    -- MB-1761-01-BET-0031
    (124, N'Produkt', NULL, 4, 1, 7, NULL),         -- a global product: in no project's data
    (125, N'1-Beton', 1, 1, 1, 1, NULL);            -- another project's
INSERT dbo.MaterialParameterValues VALUES
    (123, 140, 13, N'C35/45'),
    (125, 140, 1, N'C12/15');
INSERT dbo.AuditLogs VALUES
    (1, '2026-11-18T10:00:00', N'A. Tester', N'ProjectObjectParameter', N'13|69|61'),
    (2, '2026-11-18T14:28:00', N'B. Tester', N'ProjectObjectParameter', N'13|69|61'),
    (3, '2026-11-18T15:00:00', N'A. Tester', N'MaterialParameterValue', N'123|140'),
    (4, '2026-11-18T16:00:00', N'A. Tester', N'User', N'5');

/*
  Reference data. Idempotent: matched on the stable business CODE, never on identity values.
  Descriptive columns (Name / DisplayName / Description) are owned by this deployment and are
  re-aligned on every publish; no row is ever deleted.
*/

-- Roles
MERGE [sec].[Role] AS target
USING (VALUES
    (N'Administrator',                 N'admin'),
    (N'Administrative (gestionnaire)', N'operational'),
    (N'Guest',                         N'guest')
) AS source ([Name], [Code])
    ON target.[Code] = source.[Code]
WHEN MATCHED AND target.[Name] <> source.[Name] THEN
    UPDATE SET target.[Name] = source.[Name]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Name], [Code])
    VALUES (source.[Name], source.[Code]);
GO

-- Demand statuses (codes are part of the API contract: pending | ok | fail)
MERGE [dbo].[Status] AS target
USING (VALUES
    (N'Done',    N'Traitée',     N'ok',      N'Demand done'),
    (N'Pending', N'En cours',    N'pending', N'Demand still in process'),
    (N'Fail',    N'En anomalie', N'fail',    N'Failed demand')
) AS source ([Name], [DisplayName], [Code], [Description])
    ON target.[Code] = source.[Code]
WHEN MATCHED AND (
        target.[Name] <> source.[Name]
        OR target.[DisplayName] <> source.[DisplayName]
        OR ISNULL(target.[Description], N'') <> source.[Description]) THEN
    UPDATE SET
        target.[Name] = source.[Name],
        target.[DisplayName] = source.[DisplayName],
        target.[Description] = source.[Description]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Name], [DisplayName], [Code], [Description])
    VALUES (source.[Name], source.[DisplayName], source.[Code], source.[Description]);
GO

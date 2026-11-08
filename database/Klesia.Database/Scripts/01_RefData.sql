-- Role
MERGE [sec].[Role] AS target
USING (VALUES
    (N'Administrator',                  N'admin' ),
    (N'Administrative (gestionnaire)',  N'operational'),
    (N'Guest',                          N'guest')
) AS source ([Name], [Code])
ON target.[Name] = source.[Name]
WHEN NOT MATCHED THEN
    INSERT ([Name], [Code])
    VALUES (source.[Name], source.[Code]);
GO

-- Status
MERGE [dbo].[Status] AS target
USING (VALUES
    (N'Done', N'Traitée', N'ok', N'Demand done' ),
    (N'Pending', N'En cours', N'pending', N'Demand still in process'),
    (N'Fail', N'En anomalie', N'fail', N'Failed demand')
) AS source ([Name], [DisplayName], [Code], [Description])
ON target.[Code] = source.[Code]
WHEN NOT MATCHED THEN
    INSERT ([Name], [DisplayName], [Code], [Description])
    VALUES (source.[Name], source.[DisplayName], source.[Code], source.[Description]);
GO
CREATE TABLE [dbo].[Demand]
(
    [DemandId] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [SirenNumber] CHAR(9) NOT NULL,
    [BaeLink] NVARCHAR(2048) NOT NULL,
    [StatusId] INT NOT NULL,
    CONSTRAINT [PK_Demand] PRIMARY KEY CLUSTERED ([DemandId]),
    CONSTRAINT [FK_Demand_User] FOREIGN KEY ([UserId]) REFERENCES [sec].[User] ([UserId]),
    CONSTRAINT [FK_Demand_Status] FOREIGN KEY ([StatusId]) REFERENCES [ref].[Status] ([StatusId]),
    -- Exactly nine ASCII digits (a SIREN is NOT unique per demand: several demands may target one company).
    CONSTRAINT [CK_Demand_SirenNumber] CHECK
        ([SirenNumber] COLLATE Latin1_General_100_BIN2 LIKE '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
    CONSTRAINT [CK_Demand_BaeLink] CHECK ([BaeLink] LIKE N'https://%')
);
GO

CREATE NONCLUSTERED INDEX [IX_Demand_UserId]
    ON [dbo].[Demand] ([UserId], [DemandId] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_Demand_StatusId]
    ON [dbo].[Demand] ([StatusId]);
GO

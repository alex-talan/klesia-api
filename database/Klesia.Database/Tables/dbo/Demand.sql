CREATE TABLE [dbo].[Demand]
(
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [GKNumber] CHAR(18) NOT NULL,
    [BaeLink] NVARCHAR(2048) NOT NULL,
    [StatusId] INT NOT NULL,
    CONSTRAINT [PK_Demand] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Demand_User] FOREIGN KEY ([UserId]) REFERENCES [sec].[User] ([Id]),
    CONSTRAINT [FK_Demand_Status] FOREIGN KEY ([StatusId]) REFERENCES [ref].[Status] ([Id]),
    -- GK number: 'GK' + 6 digits (YYMMDD date) + '-' + 9 digits, 18 characters (a GK number is NOT unique per demand).
    CONSTRAINT [CK_Demand_GKNumber] CHECK
        ([GKNumber] COLLATE Latin1_General_100_BIN2 LIKE 'GK[0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
    CONSTRAINT [CK_Demand_BaeLink] CHECK ([BaeLink] LIKE N'https://%')
);
GO

CREATE NONCLUSTERED INDEX [IX_Demand_UserId]
    ON [dbo].[Demand] ([UserId], [Id] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_Demand_StatusId]
    ON [dbo].[Demand] ([StatusId]);
GO

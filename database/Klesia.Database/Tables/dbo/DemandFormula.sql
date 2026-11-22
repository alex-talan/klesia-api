CREATE TABLE [dbo].[DemandFormula]
(
    [Id] INT IDENTITY(1,1) NOT NULL,
    [DemandInformationId] INT NOT NULL,
    [Name] NVARCHAR(300) NOT NULL,
    [Detail] NVARCHAR(1000) NULL,
    CONSTRAINT [PK_DemandFormula] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_DemandFormula_DemandInformation] FOREIGN KEY ([DemandInformationId]) REFERENCES [dbo].[DemandInformation] ([Id]),
    CONSTRAINT [CK_DemandFormula_Name] CHECK (LEN(LTRIM(RTRIM([Name]))) > 0)
);
GO

CREATE NONCLUSTERED INDEX [IX_DemandFormula_DemandInformationId]
    ON [dbo].[DemandFormula] ([DemandInformationId], [Id]);
GO
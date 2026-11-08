CREATE TABLE [sec].[Role]
(
    [RoleId] INT IDENTITY NOT NULL, 
    [Name] NVARCHAR(300) NOT NULL,
    [Code] NVARCHAR(50) NOT NULL,
    CONSTRAINT [PK_Role] PRIMARY KEY ([RoleId]),
    CONSTRAINT [AK_Role_Code] UNIQUE ([Code])
);
GO

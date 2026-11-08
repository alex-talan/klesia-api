CREATE TABLE [sec].[User]
(
    [UserId] INT IDENTITY NOT NULL,
    [UserUniqueId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] INT NOT NULL CONSTRAINT [DF_User_RoleId] DEFAULT 1,
    [Name] NVARCHAR(300) NOT NULL,
    [Email] NVARCHAR(300) NOT NULL,
    [IsInternal] BIT NOT NULL CONSTRAINT [DF_User_IsInternal] DEFAULT 1,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_User_IsActive] DEFAULT 1,
    CONSTRAINT [PK_User] PRIMARY KEY ([UserId]),
    CONSTRAINT [FK_User_Role] FOREIGN KEY ([RoleId]) REFERENCES [sec].[Role]([RoleId]),
    CONSTRAINT [AK_User_UserUniqueId] UNIQUE ([UserUniqueId])
);
GO

CREATE NONCLUSTERED INDEX [IX_User]
ON [sec].[User]([UserId]) INCLUDE ([IsInternal], [RoleId])

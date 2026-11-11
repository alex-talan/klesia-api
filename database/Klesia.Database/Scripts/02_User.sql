/*
  Authorized users (allowlist). Idempotent and insert-only:
  - matched on the Entra object id (UserUniqueId), never on the identity value;
  - the role is resolved by its stable code;
  - an existing row is NEVER updated, so a user deactivated by an administrator is not
    reactivated and administrator-edited fields are not overwritten by a re-publish;
  - no user is ever deleted.
  Add further authorized identities by appending rows to the VALUES list below.
*/
MERGE [sec].[User] AS target
USING (
    SELECT
        v.[UserUniqueId],
        r.[Id] AS [RoleId],
        v.[Name],
        v.[Email],
        v.[IsInternal],
        v.[IsActive]
    FROM (VALUES
        (CONVERT(UNIQUEIDENTIFIER, N'0ce1a6e9-d434-409b-9a53-184e84c1ab36'),
         N'admin',
         N'Alejandro REYES AMARO',
         N'alejandro.reyes-amaro@talan.com',
         CONVERT(BIT, 1),
         CONVERT(BIT, 1))
    ) AS v ([UserUniqueId], [RoleCode], [Name], [Email], [IsInternal], [IsActive])
    INNER JOIN [sec].[Role] AS r
        ON r.[Code] = v.[RoleCode]
) AS source
    ON target.[UserUniqueId] = source.[UserUniqueId]
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([UserUniqueId], [RoleId], [Name], [Email], [IsInternal], [IsActive])
    VALUES (source.[UserUniqueId], source.[RoleId], source.[Name], source.[Email], source.[IsInternal], source.[IsActive]);
GO

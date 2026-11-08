MERGE [sec].[User] AS target
USING (VALUES
    (
		N'0ce1a6e9-d434-409b-9a53-184e84c1ab36', 
		1, 
		N'Alejandro REYES AMARO',
    	N'alejandro.reyes-amaro@talan.com', 	 	
		1,
		1
	)
) AS source (
	[UserUniqueId],
	[RoleId],
	[Name],
	[Email],
	[IsInternal],
	[IsActive]
)
ON target.[UserUniqueId] = source.[UserUniqueId]
WHEN NOT MATCHED THEN
    INSERT (
		[UserUniqueId],
		[RoleId],
		[Name],
		[Email],
		[IsInternal],
		[IsActive]
	)
    VALUES (
		source.[UserUniqueId],
		source.[RoleId],
		source.[Name],
		source.[Email],
		source.[IsInternal],
		source.[IsActive]
	);
GO

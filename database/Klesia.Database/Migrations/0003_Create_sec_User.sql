-- sec_User: allowlist of authorized users.
-- UserUniqueId is the Microsoft Entra object id (oid), stored as CHAR(36) in the canonical
-- 8-4-4-4-12 form; CK_User_UserUniqueId keeps the guarantee that UNIQUEIDENTIFIER gave on SQL Server.
-- IsInternal / IsActive are TINYINT(1) booleans restricted to 0 or 1 (CK_User_Flags), as BIT was.
-- Defaults are not named in MySQL. They are kept from the original design (not fail-closed, see the specification).
CREATE TABLE `sec_User`
(
    `Id`           INT          NOT NULL AUTO_INCREMENT,
    `UserUniqueId` CHAR(36)     NOT NULL,
    `RoleId`       INT          NOT NULL DEFAULT 1,
    `Name`         VARCHAR(300) NOT NULL,
    `Email`        VARCHAR(300) NOT NULL,
    `IsInternal`   TINYINT(1)   NOT NULL DEFAULT 1,
    `IsActive`     TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (`Id`),
    CONSTRAINT `AK_User_UserUniqueId` UNIQUE (`UserUniqueId`),
    KEY `IX_User_RoleId` (`RoleId`),
    CONSTRAINT `FK_User_Role` FOREIGN KEY (`RoleId`) REFERENCES `sec_Role` (`Id`),
    CONSTRAINT `CK_User_UserUniqueId` CHECK
        (REGEXP_LIKE(`UserUniqueId`, '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')),
    CONSTRAINT `CK_User_Flags` CHECK (`IsInternal` IN (0, 1) AND `IsActive` IN (0, 1))
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

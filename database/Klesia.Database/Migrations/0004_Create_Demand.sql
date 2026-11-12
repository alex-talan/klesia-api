-- Demand: business data (the former dbo schema has no prefix). A GK number is NOT unique per demand.
-- GK number: 'GK' + 6 digits (YYMMDD date) + '-' + 9 digits, 18 characters. The pattern is matched case-sensitively
-- (match type c) and the length guard rejects anything that is not exactly 18 characters.
-- IX_Demand_UserId leads with the foreign-key column; its descending Id serves the API ordering (Id DESC).
-- No ON DELETE / ON UPDATE action: InnoDB's default (RESTRICT) behaves like SQL Server's NO ACTION.
CREATE TABLE `Demand`
(
    `Id`       INT           NOT NULL AUTO_INCREMENT,
    `UserId`   INT           NOT NULL,
    `GKNumber` CHAR(18)      NOT NULL,
    `BaeLink`  VARCHAR(2048) NOT NULL,
    `StatusId` INT           NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_Demand_UserId` (`UserId`, `Id` DESC),
    KEY `IX_Demand_StatusId` (`StatusId`),
    CONSTRAINT `FK_Demand_User` FOREIGN KEY (`UserId`) REFERENCES `sec_User` (`Id`),
    CONSTRAINT `FK_Demand_Status` FOREIGN KEY (`StatusId`) REFERENCES `ref_Status` (`Id`),
    CONSTRAINT `CK_Demand_GKNumber` CHECK
        (REGEXP_LIKE(`GKNumber`, '^GK[0-9]{6}-[0-9]{9}$', 'c') AND CHAR_LENGTH(`GKNumber`) = 18),
    CONSTRAINT `CK_Demand_BaeLink` CHECK (`BaeLink` LIKE 'https://%')
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

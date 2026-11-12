-- ref_Status: demand status lookup. The Code is the API contract: pending | ok | fail.
CREATE TABLE `ref_Status`
(
    `Id`          INT          NOT NULL AUTO_INCREMENT,
    `Name`        VARCHAR(100) NOT NULL,
    `DisplayName` VARCHAR(100) NOT NULL,
    `Code`        VARCHAR(20)  NOT NULL,
    `Description` VARCHAR(500) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `AK_Status_Code` UNIQUE (`Code`),
    CONSTRAINT `CK_Status_Code` CHECK (`Code` COLLATE utf8mb4_bin IN ('pending', 'ok', 'fail'))
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

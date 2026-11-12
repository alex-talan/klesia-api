-- sec_Role: roles of the allowlist. No role grants any permission in Sprint 1 (OPEN-04).
-- MySQL has no schemas inside a database: the former [sec] schema is the "sec_" table prefix (DEC-14).
-- DDL is not transactional in MySQL: keep ONE object per migration script so a failure never leaves a half-applied script.
CREATE TABLE `sec_Role`
(
    `Id`   INT          NOT NULL AUTO_INCREMENT,
    `Name` VARCHAR(300) NOT NULL,
    `Code` VARCHAR(50)  NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `AK_Role_Code` UNIQUE (`Code`)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4 COLLATE = utf8mb4_0900_ai_ci;

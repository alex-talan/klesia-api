-- Reference data. Runs after EVERY deployment, so it is idempotent: rows are matched on the stable business
-- Code, never on an identity value. Descriptive columns are owned by this deployment and re-aligned on every run.
-- No row is ever deleted. Rows are inserted only when missing (no INSERT IGNORE, no ON DUPLICATE KEY UPDATE)
-- so a re-run neither hides real errors nor burns AUTO_INCREMENT values.
-- The literals rely on the connection collation being the utf8mb4 default (utf8mb4_0900_ai_ci), as on Azure.
-- Never write a semicolon inside a comment or a string literal: the DbUp script splitter cuts statements on it.

-- Roles
UPDATE `sec_Role` AS r
INNER JOIN (
    SELECT 'Administrator' AS `Name`, 'admin' AS `Code`
    UNION ALL SELECT 'Administrative (gestionnaire)', 'operational'
    UNION ALL SELECT 'Guest', 'guest'
) AS s ON s.`Code` = r.`Code`
SET r.`Name` = s.`Name`
WHERE r.`Name` COLLATE utf8mb4_bin <> s.`Name`;

INSERT INTO `sec_Role` (`Name`, `Code`)
SELECT s.`Name`, s.`Code`
FROM (
    SELECT 'Administrator' AS `Name`, 'admin' AS `Code`
    UNION ALL SELECT 'Administrative (gestionnaire)', 'operational'
    UNION ALL SELECT 'Guest', 'guest'
) AS s
WHERE NOT EXISTS (SELECT 1 FROM `sec_Role` AS r WHERE r.`Code` = s.`Code`);

-- Demand statuses (codes are part of the API contract: pending, ok, fail)
UPDATE `ref_Status` AS r
INNER JOIN (
    SELECT 'Done' AS `Name`, 'Traitée' AS `DisplayName`, 'ok' AS `Code`, 'Demand done' AS `Description`
    UNION ALL SELECT 'Pending', 'En cours', 'pending', 'Demand still in process'
    UNION ALL SELECT 'Fail', 'En anomalie', 'fail', 'Failed demand'
) AS s ON s.`Code` = r.`Code`
SET r.`Name` = s.`Name`,
    r.`DisplayName` = s.`DisplayName`,
    r.`Description` = s.`Description`
WHERE r.`Name` COLLATE utf8mb4_bin <> s.`Name`
   OR r.`DisplayName` COLLATE utf8mb4_bin <> s.`DisplayName`
   OR COALESCE(r.`Description`, '') COLLATE utf8mb4_bin <> s.`Description`;

INSERT INTO `ref_Status` (`Name`, `DisplayName`, `Code`, `Description`)
SELECT s.`Name`, s.`DisplayName`, s.`Code`, s.`Description`
FROM (
    SELECT 'Done' AS `Name`, 'Traitée' AS `DisplayName`, 'ok' AS `Code`, 'Demand done' AS `Description`
    UNION ALL SELECT 'Pending', 'En cours', 'pending', 'Demand still in process'
    UNION ALL SELECT 'Fail', 'En anomalie', 'fail', 'Failed demand'
) AS s
WHERE NOT EXISTS (SELECT 1 FROM `ref_Status` AS r WHERE r.`Code` = s.`Code`);

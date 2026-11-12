-- Authorized users (allowlist). Idempotent and insert-only:
--  - matched on the Entra object id (UserUniqueId), never on the identity value
--  - the role is resolved by its stable code
--  - an existing row is NEVER updated, so a user deactivated by an administrator is not reactivated
--    and administrator-edited fields are not overwritten by a re-run
--  - no user is ever deleted
-- Add further authorized identities by appending a UNION ALL SELECT line to the derived table below.
-- UserUniqueId must be the lower-case canonical Entra oid.
INSERT INTO `sec_User` (`UserUniqueId`, `RoleId`, `Name`, `Email`, `IsInternal`, `IsActive`)
SELECT v.`UserUniqueId`, r.`Id`, v.`Name`, v.`Email`, v.`IsInternal`, v.`IsActive`
FROM (
    SELECT '0ce1a6e9-d434-409b-9a53-184e84c1ab36' AS `UserUniqueId`,
           'admin' AS `RoleCode`,
           'Alejandro REYES AMARO' AS `Name`,
           'alejandro.reyes-amaro@talan.com' AS `Email`,
           1 AS `IsInternal`,
           1 AS `IsActive`
) AS v
INNER JOIN `sec_Role` AS r ON r.`Code` = v.`RoleCode`
WHERE NOT EXISTS (SELECT 1 FROM `sec_User` AS u WHERE u.`UserUniqueId` = v.`UserUniqueId`);

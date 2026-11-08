/*
  Single post-deployment entry point. Runs after EVERY successful publish, so every
  included script MUST be idempotent. Keep the order: reference data first, users second.
  Paths are relative to the project root (this file lives at the project root).
*/
:r .\Scripts\01_RefData.sql
:r .\Scripts\02_User.sql

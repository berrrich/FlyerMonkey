# Mission 009J local Reviewer checks

Restore a fresh local database using tools/Mission009J/Restore-VerifiedBacpac.ps1 without
-RehearseCleanup. Pass its generated Mission009J_Disposable_<12 hex characters> name to this
test executable. The server is fixed to (localdb)\MSSQLLocalDB; production settings are never
used as the SQL destination.

See [the mission report](../../docs/mission009j-local-validation.md) for build/run commands,
results and safety/validation limits. Each run requires the original archive baseline and
retains its SQL database and isolated synthetic SQLite fixtures.

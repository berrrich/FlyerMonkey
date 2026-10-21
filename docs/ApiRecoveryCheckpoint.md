# API recovery checkpoint — 21 October 2026 (Perth)

Status: capture helper prepared; live recovery ZIP NOT created. No Azure CLI/Az modules or saved credential was available. Azure backup/history cannot be authenticated and verified in this session. Do not deploy until a real checkpoint or independently verified Azure restore point is available.

## Capture

Use the operator's existing authorized Azure/SCM session. Do not enable basic authentication, change settings or paste secrets into chat. Kudu supports GET /api/zip/site/wwwroot/ to download the deployed application without rebuilding it. Portal > FlyerMonkeyApi > Advanced Tools > Go opens SCM. An authorized GET in that session can download:

https://flyermonkeyapi-g7htasdacxfzgbcd.scm.australiaeast-01.azurewebsites.net/api/zip/site/wwwroot/

Alternatively use tools/ApiRecovery/Capture-ApiRecovery.ps1 with a short-lived Entra bearer token accepted by SCM:

```powershell
$recoveryToken = Read-Host 'SCM-authorized access token' -AsSecureString
./tools/ApiRecovery/Capture-ApiRecovery.ps1 -AccessToken $recoveryToken
Remove-Variable recoveryToken
```

Do not run capture during another deployment. Default output is ignored DATA/ApiRecovery/FlyerMonkeyApi-<UTC timestamp>/. Treat the whole ZIP as confidential: deployed configuration files may contain secrets. Store on protected/encrypted storage, do not add to Git, and retain a protected second copy. The helper makes only one authenticated GET and writes locally. It hashes every file stream, checks required binaries/web.config and approval JSON, and records the ZIP SHA-256. This proves archive readability and required contents, not bootability or a consistent atomic snapshot. No production restore is performed.

## Independent Azure recovery checks (operator, read-only)

In FlyerMonkeyApi > Backups, inspect actual successful automatic/custom restore points, timestamps, eligibility and available restore options. Record the selected timestamp outside secrets. Automatic backups are not downloadable; custom backups can have downloadable ZIP/XML metadata. Do not configure or trigger a new backup under this authorization. The publish profile's EnableMSDeployBackup=true is intent, not evidence of an existing backup; inspect existing Web Deploy backup records if available.

Inspect Deployment Center/Kudu deployment history and retained deployment packages. A successful history entry/log is not a downloadable recovery artifact. Count it as an independent path only if the corresponding package or supported redeploy/restore source actually exists. Do not press Redeploy/Restore now.

## Configuration and external state

Capture a nonsecret inventory separately: runtime/bitness, Production environment, feature flags, approved artwork service URI, identity, storage/SQL network access and RBAC, and any package/startup settings. Secret values such as FLYERMONKEY_SQL_CONNECTION, ConnectionStrings__BlobStorage, OpenAI__ApiKey (if configured), publishing credentials and Key Vault references must remain in the existing secret manager or a protected encrypted operator export, never this report or Git. File backups do not preserve managed identity, RBAC, Key Vault secrets, SQL data or Blob artwork. Do not rotate or change them for this deployment.

Today's intended API security delta touches ProductsController and ProductImageService only: removal of API POST, image container confinement. No migration, schema or intended data change. Program.cs contains no startup migration. Existing unrelated Shared edits can be included in a publish; review the publish preview.

## Restore (requires separate authorization)

1. Confirm target FlyerMonkeyApi, resource group flyerMonkeyResourceGroup. Verify ZIP SHA-256 against checkpoint.json and each file against file-hashes.json; preserve diagnostics first.
2. Prefer an existing supported App Service backup restore point with content-only restore when settings must remain unchanged. Do not restore a database. Restore first to an existing suitable slot if available; do not create resources without approval.
3. For the local ZIP, use an approved built-artifact deployment method (Kudu ZIP Deploy/App Service ZIP deployment), with application files at archive root. Do not recompile old source or use the ZIP download API's PUT as a full cleanup strategy. Confirm existing build/package settings and review stale files; deployment can restart the app. The security deployment's normal method remains Visual Studio MSDeploy; a plain wwwroot ZIP is not an MSDeploy package.
4. Preserve current production settings, approval manifest, identity and external SQL/Blob data. Recheck the restored manifest hash.
5. GET /api/products, /api/offers, /api/offers/current, /api/offers/previous; GET /api/products/139/image; GET /api/products/102/approved-artwork/image (JPEG and approved hash); GET /api/images/artwork?productId=102 must remain 404. Send no POST, generation or fixture requests. Verify guards/route metadata from the artifact and offline tests.

Restoring the pre-security build also restores its unauthenticated product POST and arbitrary-container image risk. Prefer a corrected forward deployment where possible; operator must explicitly accept this rollback consequence.

References:
- https://github.com/projectkudu/kudu/wiki/REST-API#zip
- https://learn.microsoft.com/en-us/azure/app-service/manage-backup
- https://learn.microsoft.com/en-us/azure/app-service/deploy-zip

# Mission 010B Stage 1: product-write and image-access boundary

Local implementation only. No deployment, Azure configuration, SQL schema, Reviewer workflow or Crater guard changes.

## Client investigation and compatibility

Repository client search found no caller of POST /api/products. ProductApiService is GET-only. Windows Reviewer MainWindow commits through ProductRepository.AddProductAsync directly; ProductImageUploadService uploads to product-images and returns product-images/<filename>. Those paths and direct commits are unchanged. External callers cannot be established from repository source.

The API POST action is removed: POST /api/products now returns 405. No authentication rollout or configuration change is required. Any external caller relying on it must migrate to an explicitly designed administrative interface before writes can be restored.

ProductImageService always selects the literal product-images container. Stored paths are untrusted: exact case-sensitive prefix required; empty/dot/traversal segments, segment-edge whitespace, controls, backslashes, percent escapes, URI/query/fragment syntax are rejected before Blob access. Ordinary filenames, spaces within names and safe nested paths remain supported. Legacy nonconforming paths now return 404; no database records are rewritten. This is container confinement, not content approval for objects already inside product-images.

## Validation

- ProductSecurity.Checks: 47 passing checks. Real MVC routing on an ephemeral loopback host, fake SQL repositories and a real Blob SDK with an in-memory HTTP handler; no Azure or database access. Anonymous POST is 405 with no writes; public product/offer GETs succeed; canonical JPEG bytes survive; missing images return 404; private/other-container and malformed paths return 404 without storage requests.
- CraterIt.Checks: 112 passed, including Development/loopback/header restrictions.
- API build --no-restore: passed, zero warnings/errors.
- NuGet vulnerability lookup unavailable (NU1900); dependency audit not verified.

Commands:

```powershell
dotnet run --project tests/ProductSecurity.Checks --no-restore --no-launch-profile
dotnet run --project tests/CraterIt.Checks --no-restore --no-launch-profile -p:UseAppHost=false
dotnet build FlyerMonkey.Api/FlyerMonkey.Api.csproj --no-restore -v minimal
```

## Eventual deployment (separate approval)

Review and package the API changes with a known rollback artifact. Before deployment, assess external POST consumers and inspect existing catalog image paths read-only for compatibility. No new settings, roles, schema or mobile/Reviewer deployment are required. After approved API deployment, verify public catalog/offers and a known canonical image with GETs, confirm private Crater endpoints remain denied, and verify the deployed route table/package no longer contains product POST. Do not test production with write requests. A rollback to the old API reintroduces the write/path risks and needs an explicit security decision.

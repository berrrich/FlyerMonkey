# Mission 013B proposal

The shared ProductDetail page displays Product.ID under the product name in both Web and Android. The model property is named ID. The line uses the loaded model value.

## Proposed SQL migration

[013B_001_UserRoles.review.sql](../database/migrations/013B_001_UserRoles.review.sql) is prepared for review and defaults to refusing execution. It creates Users, Roles and UserRoles, adds nullable Customers.UserId with a foreign key and a filtered unique index, and proposes the six requested role seeds. Execution is outside Mission 013B.

Users identifies each authenticated account by a unique external tenant/object pair. Email and display name are profile details and can change. Email is nullable and has no uniqueness or account-linking rule. The application should refresh these details from authenticated identity evidence when later implemented. Authentication credentials stay with the existing identity provider.

The optional Customer relationship permits a User without a Customer and existing Customers without a User. A Customer can link to one User, and a User can link to at most one Customer. Future linking must compare the authenticated tenant/object pair with the existing Customer identity. No email-only matching or cross-tenant account merging is proposed.

The migration inserts no Users or UserRoles and does not backfill Customer links. Existing CustomerIds, tenant/object identities, status, CraterEnabled values, timestamps and artwork ownership remain intact. There are no cascading deletes. Artwork continues to use its existing CustomerId or operator tenant/object ownership keys.

## Proposed roles and assignments

| Role | Intended responsibility |
| --- | --- |
| Reader | Browse catalogue and read permitted content. Grants no generation permission. |
| Customer | Customer account and access to that customer's existing private artwork. |
| Crater Tester | Explicit eligibility for gated development customer generation. |
| Developer | Development responsibilities. Grants no implicit paid-generation permission. |
| Administrator | User and role administration when implemented. Grants no implicit paid-generation permission. |
| Artwork Reviewer | Artwork review responsibilities. Future approval actions still require explicit server authorization. |

| Authenticated account | Planned roles | Customer relationship |
| --- | --- | --- |
| richard.berriman@gmail.com | Customer, Crater Tester, Developer, Administrator | Link existing customer fingerprint 591640994422 by exact identity. |
| berrimar999@gmail.com | Reader | Preserve existing customer fingerprint 6E37B38C6D27 and link by exact identity. |
| fm.operator@richardberriman.onmicrosoft.com | Administrator | User only. Existing operator authentication/policy remains its Operator designation. |

These assignments are proposals. Operator is not an additional seed among the six requested roles. There is no implied role hierarchy or automatic privilege inheritance. A Reader-only account receives no generation grant.

Mission 013A verified the customer accounts in tenant bc2e39c7-a794-4172-b5a6-ad9bdb5cd7ab, with object IDs 73bd7664-83c7-4a58-9005-ae3f8a80af3d for richard.berriman@gmail.com and 5c65f998-22c5-4a23-97cc-ea1bbd7ba48e for berrimar999@gmail.com. The operator account is object 587c6244-6c78-4917-a953-b880a41a4abb in tenant 4112a2a6-70d8-47f0-a0fe-352edd57cc04. Revalidate these pairs before any later approved assignment.

## Minimal Reader generation protection to implement later

The current customer generation controller checks CraterEnabled and its local gates/allowlist, without SQL roles. Adding role tables or assignments alone will not enforce Reader restrictions.

Extend the existing customer repository read to resolve the Active User and role membership using the authenticated tenant/object pair and Customers.UserId. Require the Customer and Crater Tester roles, an Active Customer, and CraterEnabled=true. A missing link, disabled/missing User, missing role, or lookup failure must deny generation.

Use that same calculated permission for GET attempt CanGenerate and recheck it for POST attempt before reservation or provider resolution. Web and Android already consume CanGenerate and the existing POST, so the API supplies the enforcement. Keep owner-authorized existing-image reads available independently of the generation grant. The operator route retains its existing explicit permissions and local/production gates.

Required later offline checks cover Reader-only with CraterEnabled=true receiving CanGenerate=false and POST 403 with zero reservations/provider calls, missing/disabled/mismatched User links, a valid Customer plus Crater Tester under the existing gates, removal of a role between GET and POST, and preservation of existing private previews. Role enforcement is a proposal in this mission.

During Mission 013B, local API/Web CustomerCrater:Enabled, CraterIt:Enabled and CraterIt:LocalDevelopmentGenerationEnabled are set false, with explicit false overrides on running local profiles. The generated migration is never connected to SQL or executed by validation.

## Validation

Web and Android x64 builds passed with zero errors and the two existing unused Offers fields as warnings. The existing ApprovedArtwork.Checks suite passed all 132 checks, including rendering the full ProductDetail component through its Web and Android branches for model IDs 139, 175 and 246. These checks verify rendered content with offline services. Interactive browser/device visual verification was not performed in this mission.

The migration passed local T-SQL syntax parsing with zero execution. Runtime migration validation remains pending approval. A normal HTTPS GET to the local customer Crater attempt endpoint returned 404 with generation gates disabled. No SQL writes, Azure deployment, paid generation or Git operations were performed.

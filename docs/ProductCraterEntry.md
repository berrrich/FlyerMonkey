# Mission 010C-B: Web product Crater It entry

The existing `/products/{Id:int}` page now shows a Web-only Crater It panel.
Its GET button reloads the same product with `?crater=true` to display an explanation.
Anonymous visitors are told that public customer sign-in is coming later; no operator
login is opened. Signed-in operators see a restricted state, explicitly distinguishing
ViewOwn from Generate. Generation remains unavailable in this entry point.

The numeric Product ID supplies both the form action and the local return link.
No supplied `returnUrl`, external destination or operator redirect is consumed.
The Web host supplies authentication presentation state only; the component neither
acquires tokens nor calls the API. Web hides the legacy generation/history controls
even if a legacy local generation client is enabled. Other hosts retain their existing
controls. Public product/offer clients and API security code are unchanged.

## Manual local check

1. Restart the local Web/API using **FM Operator Local**, then browse anonymously to
   `https://localhost:7094/products/102`.
2. Click **☄️ Crater It**. Expect `/products/102?crater=true`, the customer sign-in
   explanation, and a disabled generation action. No Microsoft login should open.
3. Choose **Back to this product**. Expect `/products/102`. Repeat with another
   existing Product ID. Adding `&returnUrl=https://example.com` to the explanation
   URL must not alter either the form target or the return link.
4. Continue browsing `/products` and `/offers` anonymously.
5. Explicitly sign in through `/operator/signin`, then revisit the product and click
   Crater It. Expect the operator restriction message and no generation controls.
6. Confirm `/operator/access` retains the verified ViewOwn-only permissions and
   production generation block. Sign out using `/operator`, then repeat step 2.

## Offline validation

Web and API builds passed using `--no-restore -p:UseAppHost=false` with isolated
output under ignored `DATA/Mission010CB` because the active F5 app locks its DLLs.
The running application was not stopped. Test suites passed:

- WebAuthentication.Checks: 67, including rendered entry/explanation, operator
  restriction, Product ID, canonical return links and disabled-auth host behavior.
- ApprovedArtwork.Checks: 126, including actual shared product-detail rendering,
  public product/offer content and suppression of legacy controls on Web.
- Authentication.Checks: 47, covering API identity, authorization and ownership.
- ProductSecurity.Checks: 47, using fake SQL and offline Blob transport.
- CraterIt.Checks: 118, covering existing ownership and production hard blocks
  with fake providers.

The Web test build reports ASP0000 for deliberately isolated test service providers.
ProductSecurity.Checks reports the cached NU1900 vulnerability-feed lookup warning.
The first normal Web build hit the running process's DLL lock; isolated builds passed.
No real Entra browser integration or live generation was performed for this mission.

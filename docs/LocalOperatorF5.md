# Local operator startup with F5 (Mission 010C-A)

Select **FM Operator Local** in Visual Studio's startup dropdown and press **F5**.
Both projects' `operator-local` launch profiles now enable authentication directly;
the command-line enable flag from Mission 010B is no longer required for these profiles.
This supersedes the disabled-profile startup instructions in LocalEntraSignIn.md.
Ordinary development profiles remain unchanged and do not opt into authentication.
The operator profiles follow the ordinary profiles so they are not the default for
`dotnet run` without an explicit profile selection.

## Visual Studio setup

The existing `FlyerMonkey.slnLaunch.user` already defines **FM Operator Local** with
both projects set to **Start** and both debug targets set to **operator-local**.
It is ignored by Git and remains user-local; no solution changes are required.
If that profile is missing on another machine:

1. Open `FlyerMonkey.slnx` in Visual Studio.
2. Right-click the solution in Solution Explorer and select **Configure Startup Projects**.
3. Select **Multiple startup projects**, add a profile named **FM Operator Local**
   using **Add Profile** (the **+** button), and leave sharing disabled.
4. Set **FlyerMonkey.Api** to **Start**, with **Debug Target** = **operator-local**.
5. Set **FlyerMonkey.Web** to **Start**, with **Debug Target** = **operator-local**.
6. Set other projects to **None**, put API before Web, and select **Apply**, then **OK**.
7. Select **FM Operator Local** in the toolbar startup dropdown.

Existing local User Secrets, the trusted HTTPS development certificate and the API's
private `FLYERMONKEY_SQL_CONNECTION` prerequisite must already be available to
Visual Studio. Do not copy secrets into launch profiles or source files.

## F5 smoke test

1. Stop any manually started Web/API processes occupying ports 7094 or 7255.
   Press **F5** with **FM Operator Local** selected. Confirm both output windows
   report **Development**, with Web at `https://localhost:7094` and API at
   `https://localhost:7255`. These profiles do not automatically open a browser.
2. Browse to `https://localhost:7094/operator/signin` and complete Microsoft sign-in
   using the existing allowed operator. Expect the operator page after callback.
3. Open `https://localhost:7094/operator/access`. Expect `authenticated=true`,
   `tenantMatches=true`, `operatorMatches=true`, `Crater.ViewOwn=true`,
   Generate/Approve/Admin all false, and `productionGenerationHardBlocked=true`.
4. Return to `/operator` and click **Sign out**. Expect `/operator/signed-out`.
   Freshly request `/operator/access`: expect HTTP 401 and an explicit sign-in
   message, without automatically starting Microsoft sign-in. Microsoft logout
   remains a separate optional action.
5. While signed out, open `https://localhost:7094/products` and
   `https://localhost:7255/api/products`. Both remain public (subject to existing
   catalogue/database connectivity). Web's public catalogue still uses its existing
   published API; the direct localhost request checks the local API boundary.
6. Stop debugging with **Shift+F5**. Select the ordinary development startup targets
   when operator authentication is not wanted.

The operator profiles retain the existing tenant/client settings and external
operator allowlist, ViewOwn-only cap, local storage, and `CraterIt__Enabled=false`.
Do not send generation requests. No production settings or credentials are changed.

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.ClientModel.Primitives;
using OpenAI;
using FlyerMonkey.Api.Controllers;
using FlyerMonkey.Api.Services;
using FlyerMonkey.Shared.Model;
using FlyerMonkey.Shared.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SQLServerConnection.Data;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
var customer = Guid.NewGuid();
var customers = new Customers(customer);
var store = new MemoryStore();
var provider = new Provider();
var products = new Products();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
    ["CustomerAuthentication:Enabled"]="true", ["CustomerCrater:Enabled"]="true",
    ["CraterIt:Enabled"]="true", ["CustomerCrater:AllowedCustomerId"]=customer.ToString(),
    ["Authentication:Enabled"]="true", ["Authentication:ViewOwnOnly"]="true",
    ["CustomerCrater:CompletedAccountRetestLimit"]="0"
});
builder.Services.AddCustomerAuthentication(builder.Configuration);
using var rsa = RSA.Create(2048);
var key = new RsaSecurityKey(rsa) { KeyId = "offline" };
builder.Services.PostConfigure<JwtBearerOptions>(CustomerAuthentication.Scheme, options => {
    var metadata = new OpenIdConnectConfiguration { Issuer = CustomerIdentity.Issuer };
    metadata.SigningKeys.Add(key);
    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
    options.Backchannel = new HttpClient(new NoNetwork());
});
builder.Services.AddSingleton<ICustomerRepository>(customers);
builder.Services.AddSingleton<ICustomerCraterStore>(store);
builder.Services.AddSingleton(new ClassicCraterArtwork(
    Path.Combine(Path.GetTempPath(), "FlyerMonkey-ClassicCrater-" + Guid.NewGuid().ToString("N")), store));
builder.Services.AddSingleton<ICustomerCraterProvider>(provider);
builder.Services.AddSingleton<IProductRepository>(products);
builder.Services.AddScoped<ProductService>();
builder.Services.AddControllers().AddApplicationPart(typeof(CustomerCraterController).Assembly);
await using var app = builder.Build();
// Test-only HTTPS request metadata; no external requests, SQL, credentials or paid provider.
var testScheme="https";
var testIp=IPAddress.Loopback;
var testHost="localhost";
app.Use((context,next) => { context.Request.Scheme=testScheme; context.Connection.RemoteIpAddress=testIp; context.Request.Host=new HostString(testHost); return next(context); });
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
await app.StartAsync();
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress = new(app.Urls.Single()) };
string Token(bool wrongTenant=false, bool wrongClient=false, bool operatorIssuer=false) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
    operatorIssuer ? "https://operator.offline.invalid/v2.0" : CustomerIdentity.Issuer, CustomerIdentity.ApiClientId, new[] {
        new Claim("tid",wrongTenant ? Guid.NewGuid().ToString() : CustomerIdentity.TenantId),
        new Claim("oid",Guid.NewGuid().ToString()),new Claim("azp",wrongClient ? Guid.NewGuid().ToString() : CustomerIdentity.WebClientId),
        new Claim("ver","2.0"),new Claim("scp","Customer.Access") },
    DateTime.UtcNow.AddMinutes(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(key,SecurityAlgorithms.RsaSha256)));
var token = Token();
async Task<HttpResponseMessage> Request(string operation="attempt", bool post=false, string? bearer=null, string? body=null) {
    using var request = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get,"api/customers/crater/139/"+operation);
    if(bearer is not null) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
    if(body is not null) request.Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json");
    return await http.SendAsync(request);
}
async Task<CustomerCraterAttempt> Status() => (await (await Request(bearer:token)).Content.ReadFromJsonAsync<CustomerCraterAttempt>())!;
async Task<bool?> PreviewReadiness()
{
    var status = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("api/status");
    return status.TryGetProperty("androidPreviewReady", out var ready) ? ready.GetBoolean() : null;
}
try {
    Check(await PreviewReadiness() == true, "Local status verifies default-off preview configuration");
    Check(await Status() is { State: "Ready", CanGenerate: false }, "Default-off generation masks Ready eligibility");
    foreach (var setting in new string?[] { null, "false", "invalid" })
    {
        builder.Configuration["CustomerCrater:GenerationEnabled"] = setting;
        var readsBefore = customers.Reads;
        Check((await Request(post:true,bearer:token)).StatusCode == HttpStatusCode.NotFound &&
            customers.Reads == readsBefore && products.Reads == 0 && store.Record is null && provider.Calls == 0,
            "Generation switch blocks an eligible tester before account, product, reservation and provider access");
    }
    builder.Configuration["CustomerCrater:GenerationEnabled"] = "true";
    Check(await PreviewReadiness() == false, "Launcher status rejects customer generation enabled");
    builder.Configuration["CustomerCrater:GenerationEnabled"] = "false";
    builder.Configuration["CraterIt:LocalDevelopmentGenerationEnabled"] = "true";
    Check(await PreviewReadiness() == false, "Launcher status rejects local operator generation enabled");
    builder.Configuration["CraterIt:LocalDevelopmentGenerationEnabled"] = "false";
    builder.Configuration["Authentication:ViewOwnOnly"] = "false";
    Check(await PreviewReadiness() == false, "Launcher status rejects operator write access");
    builder.Configuration["Authentication:ViewOwnOnly"] = "true";
    builder.Configuration["CustomerCrater:Enabled"] = "false";
    Check(await PreviewReadiness() == false, "Launcher status rejects disabled preview");
    builder.Configuration["CustomerCrater:Enabled"] = "true";
    testScheme="http";
    Check(await PreviewReadiness() is null, "Non-HTTPS health response omits local readiness");
    testScheme="https";
    builder.Configuration["CustomerCrater:GenerationEnabled"] = "true";
    Check((await Request(post:true)).StatusCode==HttpStatusCode.Unauthorized,"Anonymous create denied");
    Check((await Request("image")).StatusCode==HttpStatusCode.Unauthorized,"Anonymous preview denied");
    Check((await Request(post:true,bearer:Token(operatorIssuer:true))).StatusCode==HttpStatusCode.Unauthorized,"Operator-issued token rejected by CustomerBearer");
    Check((await Request(post:true,bearer:Token(wrongTenant:true))).StatusCode==HttpStatusCode.Forbidden,"Operator tenant cannot impersonate customer");
    Check((await Request(post:true,bearer:Token(wrongClient:true))).StatusCode==HttpStatusCode.Forbidden,"Wrong client denied");
    var calls=customers.Reads;
    app.Environment.EnvironmentName="Production";
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.NotFound && customers.Reads==calls,"Production blocked before SQL/provider");
    app.Environment.EnvironmentName="Development";
    testScheme="http";
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.NotFound,"Plain HTTP fails closed");
    testScheme="https"; testIp=IPAddress.Parse("192.0.2.1");
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.NotFound,"Nonloopback origin denied");
    testIp=IPAddress.Loopback; testHost="external.invalid";
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.NotFound,"Nonlocal Host denied");
    testHost="localhost";
    foreach(var setting in new[]{"CustomerCrater:Enabled","CraterIt:Enabled","CustomerAuthentication:Enabled"}) {
        builder.Configuration[setting]=null;
        Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.NotFound,"Absent gate fails closed");
        builder.Configuration[setting]="false";
        var readsBeforeFalseGate = customers.Reads;
        Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.NotFound &&
            customers.Reads==readsBeforeFalseGate && provider.Calls==0,
            "Explicit false gate blocks an eligible authorized customer before SQL/provider");
        builder.Configuration[setting]="true";
    }
    builder.Configuration["CustomerCrater:Enabled"]="invalid";
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.ServiceUnavailable,"Malformed gate fails closed");
    builder.Configuration["CustomerCrater:Enabled"]="true";
    builder.Configuration["CustomerCrater:AllowedCustomerId"]=null;
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.Forbidden,"Missing allowlist denies");
    builder.Configuration["CustomerCrater:AllowedCustomerId"]=customer.ToString();
    customers.Account=customers.Account with {Status="Disabled"};
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.Forbidden,"Disabled customer denied");
    customers.Account=customers.Account with {Status="Active",CraterEnabled=false};
    Check(!(await Status()).CanGenerate,"False SQL eligibility disables Generate");
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.Forbidden,"False SQL eligibility denies create");
    customers.Fail=true;
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.ServiceUnavailable,"SQL failure fails closed");
    customers.Fail=false; customers.Account=customers.Account with {CraterEnabled=true};
    customers.Missing=true;
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.Forbidden,"Missing customer denied without provisioning");
    customers.Missing=false;
    foreach (var scenario in new[] { "Reader", "Customer", "Crater Tester", "Administrator", "missing", "disabled", "tenant", "subject", "customer" }) {
        customers.GrantScenario=scenario;
        Check(!(await Status()).CanGenerate,"Role/link denial masks CanGenerate: "+scenario);
        Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.Forbidden &&
            store.Record is null && provider.Calls==0,"Role/link denial makes no reservation or provider call: "+scenario);
    }
    customers.GrantScenario="valid";
    Check((await Status()).CanGenerate,"Valid linked roles permit GET under existing gates");
    customers.GrantScenario="Reader";
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.Forbidden && store.Record is null,
        "Role removed between GET and POST is rechecked before reservation");
    customers.GrantScenario="failure";
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.ServiceUnavailable && store.Record is null && provider.Calls==0,
        "Role lookup failure fails closed before reservation or provider");
    customers.GrantScenario="valid";
    Check(provider.Calls==0,"All denials resolve no paid provider calls");
    Check((await Status()).CanGenerate,"Eligible allowlisted local customer is Ready");
    Check((await Request("image",bearer:token)).StatusCode==HttpStatusCode.NotFound && provider.Calls==0,"Preview cannot initiate generation");
    provider.Block=true;
    var generating=Request(post:true,bearer:token,body:"{\"productId\":999,\"productName\":\"UNTRUSTED\",\"prompt\":\"UNTRUSTED\",\"customerId\":\"UNTRUSTED\"}");
    await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(store.Record?.State=="Generating","Reservation persisted before provider dispatch");
    Check((await Status()).State=="Generating","Concurrent status sees in-progress attempt");
    Check((await (await Request(post:true,bearer:token)).Content.ReadFromJsonAsync<CustomerCraterAttempt>())?.State=="Generating","Concurrent POST does not dispatch another call");
    provider.Release.TrySetResult();
    Check((await generating).StatusCode==HttpStatusCode.OK && provider.Calls==1,"One generation completes");
    Check(provider.Prompt.Contains("SERVER PRODUCT")&&!provider.Prompt.Contains("UNTRUSTED")&&products.Reads==2,"Prompt and product come from server catalogue");
    Check((await Status()).State=="Completed"&&(await Status()).HasImage,"Completed means committed private image");
    Check((await Request("image",bearer:token)).StatusCode==HttpStatusCode.OK,"Owner can preview");
    customers.GrantScenario="Reader";
    Check((await Request("image",bearer:token)).StatusCode==HttpStatusCode.OK && !(await Status()).CanGenerate,
        "Reader retains existing owner preview without generation permission");
    Check((await Request(post:true,bearer:token)).StatusCode==HttpStatusCode.Forbidden && provider.Calls==1,
        "Reader cannot regenerate existing artwork");
    builder.Configuration["CustomerCrater:GenerationEnabled"] = "false";
    var completedRecord = store.Record;
    var completedBytes = store.Image!.ToArray();
    using (var preview = await Request("image", bearer:token))
    {
        Check(preview.StatusCode == HttpStatusCode.OK &&
            (await preview.Content.ReadAsByteArrayAsync()).SequenceEqual(completedBytes) &&
            await Status() is { State: "Completed", HasImage: true, CanGenerate: false },
            "Preview-only mode serves the Reader's existing completed JPEG");
    }
    Check((await Request("image")).StatusCode == HttpStatusCode.Unauthorized,
        "Preview-only mode rejects anonymous image reads");
    customers.Account = customers.Account with { CustomerId = Guid.NewGuid() };
    Check((await Request("image",bearer:Token())).StatusCode == HttpStatusCode.Forbidden,
        "Preview-only mode rejects a different customer");
    customers.Account = customers.Account with { CustomerId=customer };
    Check((await Request(post:true,bearer:token)).StatusCode == HttpStatusCode.NotFound &&
        provider.Calls == 1 && store.Record == completedRecord && store.Image!.SequenceEqual(completedBytes),
        "Preview-only mode blocks Reader POST and preserves the completed journal and JPEG");
    builder.Configuration["CustomerCrater:GenerationEnabled"] = "true";
    customers.GrantScenario="failure";
    Check((await Request("image",bearer:token)).StatusCode==HttpStatusCode.OK,
        "Role storage failure does not remove existing owner preview");
    customers.GrantScenario="valid";
    await Request(post:true,bearer:token);
    Check(provider.Calls==1,"Repeated click/refresh POST cannot pay twice");
    var other=Guid.NewGuid(); customers.Account=customers.Account with {CustomerId=other};
    Check((await Request("image",bearer:token)).StatusCode==HttpStatusCode.Forbidden,"Other customer cannot access allowlisted artwork");
    builder.Configuration["CustomerCrater:AllowedCustomerId"]=other.ToString();
    Check((await Request("image",bearer:token)).StatusCode==HttpStatusCode.NotFound,"Changing allowlist does not transfer artwork ownership");
    customers.Account=customers.Account with {CustomerId=customer}; builder.Configuration["CustomerCrater:AllowedCustomerId"]=customer.ToString();
    store.FailPreview=true;
    Check((await Request("image",bearer:token)).StatusCode==HttpStatusCode.ServiceUnavailable,"Preview storage failure is safe");
    await Request("image",bearer:token); await Request(post:true,bearer:token);
    Check(provider.Calls==1,"Failed preview and repeat POST never generate again");
    store.FailPreview=false;
    const string classicImage = "api/products/139/classic-crater-image";
    Check((await http.GetAsync(classicImage)).StatusCode == HttpStatusCode.NotFound,
        "Private Crater It artwork never becomes the public catalogue image");
    builder.Configuration["CustomerCrater:ClassicEnabled"] = "true";
    Check((await Request("classic", post: true)).StatusCode == HttpStatusCode.Unauthorized &&
        (await Status()).CanGenerateClassic && !(await Status()).CanGenerate,
        "Classic generation requires sign-in and has independent eligibility");
    customers.GrantScenario = "Reader";
    Check((await Request("classic", post: true, bearer: token)).StatusCode == HttpStatusCode.Forbidden &&
        provider.Calls == 1, "Ineligible customers cannot generate public Classic artwork");
    customers.GrantScenario = "valid";
    store.HasQuota = false;
    Check((await Request("classic", post: true, bearer: token)).StatusCode == HttpStatusCode.TooManyRequests &&
        provider.Calls == 1, "Classic generation shares the daily paid quota");
    store.HasQuota = true;
    Check((await Request("classic", post: true, bearer: token)).StatusCode == HttpStatusCode.NoContent &&
        provider.Calls == 2 && provider.Prompt.Contains("on a plain background"),
        "Classic generation uses the product-faithful preset");
    using var published = await http.GetAsync(classicImage);
    Check(published.IsSuccessStatusCode && published.Content.Headers.ContentType?.MediaType == "image/jpeg" &&
        published.Headers.CacheControl?.NoStore == true && store.Record == completedRecord,
        "Anonymous visitors can read published Classic artwork while the private journal stays separate");
    Check((await http.GetAsync("api/products/140/classic-crater-image")).StatusCode == HttpStatusCode.NotFound,
        "Published catalogue images are matched by product");
    store.HasQuota = false;
    builder.Configuration["CustomerCrater:UnlimitedCustomerId"] = customer.ToString();
    Check(await Status() is { CanGenerate: true, CanGenerateClassic: false, AttemptVersion: 1 },
        "Unlimited Crater It bypasses account and daily caps while Classic stays capped");
    Check((await Request("attempt?repeat=true", post: true, bearer: token)).StatusCode == HttpStatusCode.Conflict &&
        provider.Calls == 2, "An unlimited attempt requires a fresh version");
    Check((await Request("attempt?repeat=true&version=1", post: true, bearer: token)).IsSuccessStatusCode &&
        provider.Calls == 3 && (await Status()).AttemptVersion == 2,
        "Unlimited tester can generate again when the daily quota is full");
    Check((await Request("attempt?repeat=true&version=1", post: true, bearer: token)).StatusCode == HttpStatusCode.Conflict &&
        provider.Calls == 3, "Replaying the same unlimited request cannot start another paid generation");
    customers.Account = customers.Account with { CustomerId = other };
    builder.Configuration["CustomerCrater:AllowedCustomerId"] = other.ToString();
    Check(await Status() is { State: "DailyLimitReached", CanGenerate: false, AttemptVersion: null },
        "An allowance assigned to the tester does not follow another eligible account");
    store.HasQuota = true;
    builder.Configuration["CustomerCrater:CompletedAccountRetestLimit"] = null;
    store.Record = new CustomerAttemptRecord(other, 139, "Completed", DateTimeOffset.UtcNow,
        IsRepeat: true, RetestCount: 1);
    Check(await Status() is { CanGenerate: false, AttemptVersion: null } &&
        (await (await Request("attempt?repeat=true", post: true, bearer: token)).Content
            .ReadFromJsonAsync<CustomerCraterAttempt>())?.State == "AccountLimitReached" &&
        provider.Calls == 3, "Another account cannot use a third Crater It attempt");
} finally { await app.StopAsync(); }

var product=new Product {ID=139,Name="SERVER PRODUCT"};
using(var sdkHandler=new TransientFailure())
using(var sdkHttp=new HttpClient(sdkHandler)) {
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["OpenAI:ApiKey"]="offline-dummy-key"}).Build();
    var sdkProvider=new CustomerCraterProvider(config,new OpenAIClientOptions{Transport=new HttpClientPipelineTransport(sdkHttp)});
    try {await sdkProvider.GenerateAsync("Offline retry check",CancellationToken.None);throw new Exception("Expected fake provider failure");}
    catch(System.ClientModel.ClientResultException){Check(sdkHandler.Calls==1,"Real customer SDK makes one request on retryable failure");}
}
foreach(var failure in new[]{"provider","save","configuration"}) {
    var fakeStore=new MemoryStore {FailSave=failure=="save"};
    var fakeProvider=new Provider {Fail=failure=="provider"};
    var generation=new CustomerCraterGeneration(fakeStore);
    var result=await generation.GenerateAsync(customer,product,()=>failure=="configuration" ? throw new InvalidOperationException() : fakeProvider);
    Check(result.State==(failure=="configuration"?"Failed":"Unknown")&&!result.HasImage,"Failure does not record misleading Completed: "+failure);
    await new CustomerCraterGeneration(fakeStore).GenerateAsync(customer,product,()=>fakeProvider);
    Check(fakeProvider.Calls==(failure=="configuration"?0:1),"Restarted service cannot repeat failed/uncertain attempt: "+failure);
}
// Real file journal tests use an isolated disposable fixture, never DATA or existing artwork.
var root=Path.Combine(Path.GetTempPath(),"FlyerMonkey-CustomerCrater-"+Guid.NewGuid().ToString("N"));
var disk=new CustomerCraterStore(root);
using(var lease=disk.Lock(customer)) {
    disk.Reserve(new(customer,139,"Pending",DateTimeOffset.UtcNow));
    Check(new CustomerCraterStore(root).Lock(customer) is null,"Exclusive file lease excludes another store/process");
}
var restarted=new CustomerCraterGeneration(new CustomerCraterStore(root));
Check(restarted.Status(customer,true).State=="Unknown","Restart recovers abandoned Pending as Unknown");
var never=new Provider(); await restarted.GenerateAsync(customer,product,()=>never);
Check(never.Calls==0,"Durable reservation blocks dispatch across restart");
try { using var lease=disk.Lock(customer); disk.Reserve(new(customer,139,"Pending",DateTimeOffset.UtcNow)); throw new Exception("Expected existing reservation"); }
catch(IOException) { Check(true,"Reservation cannot overwrite an existing attempt"); }
var completedCustomer=Guid.NewGuid(); var completedProvider=new Provider();
await new CustomerCraterGeneration(disk).GenerateAsync(completedCustomer,product,()=>completedProvider);
Check(new CustomerCraterGeneration(new CustomerCraterStore(root)).Status(completedCustomer,true).HasImage,"Committed image survives store reconstruction");
var retest = new CustomerCraterGeneration(disk, retestLimit: 1);
Check(retest.Status(completedCustomer, true) is { CanGenerate: true, HasImage: true } &&
    !retest.Status(completedCustomer, false).CanGenerate, "Retest requires eligibility and preserves the preview");
await retest.GenerateAsync(completedCustomer, product, () => completedProvider);
Check(completedProvider.Calls == 1, "A replayed initial request cannot start a retest");
var retestProvider = new Provider { Fail = true };
await retest.GenerateAsync(completedCustomer, product, () => retestProvider, repeat: true);
var retestRestart = new CustomerCraterGeneration(new CustomerCraterStore(root), retestLimit: 1);
await retestRestart.GenerateAsync(completedCustomer, product, () => retestProvider, repeat: true);
Check(retestProvider.Calls == 1 && retestRestart.Status(completedCustomer, true) is
    { State: "Unknown", CanGenerate: false, HasImage: true }, "Failed retest is durable and retains the prior preview");
var legacyRetestCustomer = Guid.NewGuid();
disk.Lock(legacyRetestCustomer)?.Dispose();
disk.Reserve(new(legacyRetestCustomer, 139, "Completed", DateTimeOffset.UtcNow, IsRepeat: true));
disk.SaveImage(legacyRetestCustomer, 139, [255, 216, 255, 0]);
var finalRetest = new CustomerCraterGeneration(disk, retestLimit: 2);
var finalProvider = new Provider();
Check(finalRetest.Status(legacyRetestCustomer, true).CanGenerate, "A completed legacy retest has one additional attempt");
await finalRetest.GenerateAsync(legacyRetestCustomer, product, () => finalProvider, repeat: true);
await finalRetest.GenerateAsync(legacyRetestCustomer, product, () => finalProvider, repeat: true);
Check(finalProvider.Calls == 1 && !finalRetest.Status(legacyRetestCustomer, true).CanGenerate &&
    finalProvider.Prompt.EndsWith("Put the product into planet of the apes motion picture."),
    "The additional attempt uses the requested prompt and cannot be repeated");
var history = Path.Combine(root, legacyRetestCustomer.ToString("N"), "139", "attempts");
var priorFolder = Path.Combine(history, "2");
var newFolder = Path.Combine(history, "3");
var archived = System.Text.Json.JsonSerializer.Deserialize<CustomerAttemptRecord>(
    File.ReadAllText(Path.Combine(newFolder, "metadata.json")));
Check(File.ReadAllBytes(Path.Combine(priorFolder, "artwork.jpg")).SequenceEqual(new byte[] {255, 216, 255, 0}) &&
    File.Exists(Path.Combine(newFolder, "artwork.jpg")) && archived is { State: "Completed", StartedUtc: not null } &&
    archived.Prompt == finalProvider.Prompt, "Legacy and new previews have separate folders with attempt metadata");
var failedFolder = Path.Combine(root, completedCustomer.ToString("N"), "139", "attempts", "2");
Check(File.Exists(Path.Combine(failedFolder, "metadata.json")) && !File.Exists(Path.Combine(failedFolder, "artwork.jpg")),
    "Failed generation has its own metadata and does not archive the prior preview as new artwork");
var multiStore = new CustomerCraterStore(Path.Combine(root, "all-products"));
var multiCustomer = Guid.NewGuid();
using (multiStore.Lock(multiCustomer))
{
    multiStore.Reserve(new(multiCustomer, 139, "Completed", DateTimeOffset.UtcNow, IsRepeat: true, RetestCount: 5));
    multiStore.SaveImage(multiCustomer, 139, [255, 216, 255, 0]);
}
var multiProduct = new CustomerCraterGeneration(multiStore, retestLimit: 6);
var anotherProduct = new Product { ID = 140, Name = "A different product" };
Check(multiProduct.Status(multiCustomer, true, 140) is { CanGenerate: true, HasImage: false },
    "The tester can use the additional attempt on another product");
var multiProvider = new Provider();
await multiProduct.GenerateAsync(multiCustomer, anotherProduct, () => multiProvider);
Check(multiProvider.Calls == 1 && multiProvider.Prompt.Contains(anotherProduct.Name) &&
    multiProduct.Status(multiCustomer, true, 140).HasImage &&
    multiProduct.Status(multiCustomer, true, 139) is { HasImage: true, CanGenerate: false } &&
    multiProduct.Status(multiCustomer, true, 141).State == "AccountLimitReached",
    "A new product has its own artwork and consumes the same account allowance");
foreach (var interruptedState in new[] { "Generating", "Unknown" })
{
    var interruptedCustomer = Guid.NewGuid();
    using (disk.Lock(interruptedCustomer))
    {
        disk.Reserve(new(interruptedCustomer, 139, interruptedState, DateTimeOffset.UtcNow));
        disk.SaveImage(interruptedCustomer, 139, [255, 216, 255, 0]);
    }
    var recovered = await restarted.GenerateAsync(interruptedCustomer, product, () => never);
    Check(recovered is { State: "Completed", HasImage: true, CanGenerate: false } && never.Calls == 0,
        $"Retry recovers the committed JPEG from {interruptedState} without dispatch");
    Check(restarted.Status(interruptedCustomer, false).HasImage && disk.Read(interruptedCustomer, 139)?.State == "Completed",
        $"Recovery from {interruptedState} persists and stays owner-readable without a generation grant");
}
Check(disk.ReadImage(Guid.NewGuid(),139) is null,"Other customer storage key has no image");
using(var lease=disk.Lock(customer)) {File.WriteAllText(Path.Combine(root,customer.ToString("N"),"139","attempt.json"),"invalid");}
try {await restarted.GenerateAsync(customer,product,()=>never);throw new Exception("Expected corrupt journal rejection");}
catch(InvalidDataException){Check(never.Calls==0,"Corrupt durable journal fails closed without dispatch");}
var clock = new QuotaClock { Now = new DateTimeOffset(2026, 11, 22, 23, 59, 0, TimeSpan.Zero) };
var quotaRoot = Path.Combine(root, "quota-fixture");
var quotaCustomers = Enumerable.Range(0, 16).Select(_ => Guid.NewGuid()).ToArray();
var quotaAttempts = await Task.WhenAll(quotaCustomers.Select(id => Task.Run(async () =>
    await new CustomerCraterGeneration(new CustomerCraterStore(quotaRoot), clock)
        .GenerateAsync(id, product, () => new Provider()))));
Check(quotaAttempts.Count(attempt => attempt.State == "Completed") == 10 &&
    quotaAttempts.Count(attempt => attempt.State == "DailyLimitReached") == 6,
    "Concurrent requests across store instances share exactly ten daily slots");
var blocked = quotaCustomers[Array.FindIndex(quotaAttempts, attempt => attempt.State == "DailyLimitReached")];
var quotaStore = new CustomerCraterStore(quotaRoot);
var quotaRestart = new CustomerCraterGeneration(quotaStore, clock);
Check(quotaRestart.Status(blocked, true) is { State: "DailyLimitReached", CanGenerate: false } &&
    quotaStore.Read(blocked, 139) is null, "Quota survives reconstruction without consuming the blocked customer's attempt");
clock.Now = clock.Now.AddMinutes(1);
Check(quotaRestart.Status(blocked, true).CanGenerate &&
    (await quotaRestart.GenerateAsync(blocked, product, () => new Provider())).State == "Completed",
    "A new UTC day makes capacity available");
await WebChecks.Run(Check);
Console.WriteLine($"{checks} customer Crater checks passed. Offline fixtures only; no SQL or paid calls.");

sealed class QuotaClock : TimeProvider
{
    public DateTimeOffset Now { get; set; }
    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class Customers(Guid id) : ICustomerRepository {
    public string GrantScenario="valid";
    public Task<CustomerGenerationAccess?> GetGenerationAccessAsync(Guid tenant,Guid user,CancellationToken token) {
        if(GrantScenario=="failure") throw new IOException();
        if(GrantScenario=="missing") return Task.FromResult<CustomerGenerationAccess?>(null);
        return Task.FromResult<CustomerGenerationAccess?>(new(
            GrantScenario=="customer" ? Guid.NewGuid() : Account.CustomerId,
            GrantScenario=="tenant" ? Guid.NewGuid() : tenant,
            GrantScenario=="subject" ? Guid.NewGuid() : user,
            GrantScenario=="disabled" ? "Disabled" : "Active",
            new[]{"Reader","Customer","Crater Tester","Administrator"}.Contains(GrantScenario)
                ? new[]{GrantScenario} : new[]{"Customer","Crater Tester"}));
    }
    public CustomerAccount Account=new(id,"Active",true); public bool Fail,Missing; public int Reads;
    public Task<CustomerAccount?> GetAsync(Guid tenant,Guid user,CancellationToken token){Reads++;if(Fail)throw new IOException();return Task.FromResult<CustomerAccount?>(Missing?null:Account);}
    public Task<CustomerAccount> ProvisionAsync(Guid tenant,Guid user,CancellationToken token)=>throw new Exception("Never provision during generation");
}
sealed class Products : IProductRepository {
    public int Reads;
    public Task<List<Product>> GetProductsAsync(CancellationToken token=default){Reads++;return Task.FromResult(new List<Product>{new(){ID=139,Name="SERVER PRODUCT"}});}
    public Task<int> AddProductAsync(Product product,CancellationToken token=default)=>throw new Exception("No catalogue writes");
}
sealed class Provider : ICustomerCraterProvider {
    public int Calls; public bool Fail,Block; public string Prompt="";
    public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<byte[]> GenerateAsync(string prompt,CancellationToken token){Calls++;Prompt=prompt;Entered.TrySetResult();if(Block)await Release.Task.WaitAsync(token);if(Fail)throw new TimeoutException();return [255,216,255,0];}
}
sealed class MemoryStore : ICustomerCraterStore {
    public int AttemptsUsed(Guid customer) => Record?.CustomerId == customer ? 1 + Record.RetestsUsed : 0;
    public bool HasQuota = true;
    public bool HasDailyCapacity(DateOnly day) => HasQuota;
    public bool TryReserveDailySlot(DateOnly day) => HasQuota;
    public CustomerAttemptRecord? Record; public byte[]? Image; public bool FailSave,FailPreview;
    private int locked;
    public IDisposable? Lock(Guid customer)=>Interlocked.CompareExchange(ref locked,1,0)==0 ? new Lease(()=>Interlocked.Exchange(ref locked,0)) : null;
    public CustomerAttemptRecord? Read(Guid customer,int product)=>Record?.CustomerId==customer?Record:null;
    public void Reserve(CustomerAttemptRecord record){if(Record is not null)throw new IOException();Record=record;}
    public void Write(CustomerAttemptRecord record)=>Record=record;
    public void PreservePreview(CustomerAttemptRecord record, byte[] image) { }
    public void SaveImage(CustomerAttemptRecord record, byte[] image){if(FailSave)throw new IOException();Image=image;}
    public byte[]? ReadImage(Guid customer,int product){if(FailPreview)throw new IOException();return Record?.CustomerId==customer?Image:null;}
    private sealed class Lease(Action release):IDisposable {public void Dispose()=>release();}
}
sealed class NoNetwork:HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("No provider network allowed");
}
sealed class TransientFailure:HttpMessageHandler {
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{\"error\":{\"message\":\"offline\",\"type\":\"server_error\"}}",System.Text.Encoding.UTF8,"application/json")});}
}

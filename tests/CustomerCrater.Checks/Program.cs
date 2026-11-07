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
    ["CraterIt:Enabled"]="true", ["CustomerCrater:AllowedCustomerId"]=customer.ToString()
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
try {
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
using(var lease=disk.Lock(customer,139)) {
    disk.Reserve(new(customer,139,"Pending",DateTimeOffset.UtcNow));
    Check(new CustomerCraterStore(root).Lock(customer,139) is null,"Exclusive file lease excludes another store/process");
}
var restarted=new CustomerCraterGeneration(new CustomerCraterStore(root));
Check(restarted.Status(customer,true).State=="Unknown","Restart recovers abandoned Pending as Unknown");
var never=new Provider(); await restarted.GenerateAsync(customer,product,()=>never);
Check(never.Calls==0,"Durable reservation blocks dispatch across restart");
try { using var lease=disk.Lock(customer,139); disk.Reserve(new(customer,139,"Pending",DateTimeOffset.UtcNow)); throw new Exception("Expected existing reservation"); }
catch(IOException) { Check(true,"Reservation cannot overwrite an existing attempt"); }
var completedCustomer=Guid.NewGuid(); var completedProvider=new Provider();
await new CustomerCraterGeneration(disk).GenerateAsync(completedCustomer,product,()=>completedProvider);
Check(new CustomerCraterGeneration(new CustomerCraterStore(root)).Status(completedCustomer,true).HasImage,"Committed image survives store reconstruction");
Check(disk.ReadImage(Guid.NewGuid(),139) is null,"Other customer storage key has no image");
using(var lease=disk.Lock(customer,139)) {File.WriteAllText(Path.Combine(root,customer.ToString("N"),"139","attempt.json"),"invalid");}
try {await restarted.GenerateAsync(customer,product,()=>never);throw new Exception("Expected corrupt journal rejection");}
catch(InvalidDataException){Check(never.Calls==0,"Corrupt durable journal fails closed without dispatch");}
await WebChecks.Run(Check);
Console.WriteLine($"{checks} customer Crater checks passed. Offline fixtures only; no SQL or paid calls.");

sealed class Customers(Guid id) : ICustomerRepository {
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
    public CustomerAttemptRecord? Record; public byte[]? Image; public bool FailSave,FailPreview;
    private int locked;
    public IDisposable? Lock(Guid customer,int product)=>Interlocked.CompareExchange(ref locked,1,0)==0 ? new Lease(()=>Interlocked.Exchange(ref locked,0)) : null;
    public CustomerAttemptRecord? Read(Guid customer,int product)=>Record?.CustomerId==customer?Record:null;
    public void Reserve(CustomerAttemptRecord record){if(Record is not null)throw new IOException();Record=record;}
    public void Write(CustomerAttemptRecord record)=>Record=record;
    public void SaveImage(Guid customer,int product,byte[] image){if(FailSave)throw new IOException();Image=image;}
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

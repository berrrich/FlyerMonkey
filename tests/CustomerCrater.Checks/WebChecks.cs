using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FlyerMonkey.Shared.Services;
using FlyerMonkey.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Identity.Web;

static class WebChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["CustomerCrater:Enabled"]="true",["CraterIt:Enabled"]="true"});
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAntiforgery();
        builder.Services.AddRazorComponents();
        builder.Services.AddAuthentication("Fixture").AddCookie("Fixture",options=>{
            options.Events.OnRedirectToLogin=context=>{context.Response.StatusCode=401;return Task.CompletedTask;};
            options.Events.OnRedirectToAccessDenied=context=>{context.Response.StatusCode=403;return Task.CompletedTask;};
        });
        builder.Services.AddAuthorization(options=>options.AddPolicy(CustomerAuthentication.Policy,
            policy=>policy.RequireAuthenticatedUser().RequireClaim("fixture-kind","customer")));
        builder.Services.AddSingleton(DispatchProxy.Create<ITokenAcquisition,WebTokens>());
        var transport=new WebTransport();
        builder.Services.AddSingleton(new ClassicCraterImages(new Uri("https://localhost:7255/")));
        builder.Services.AddHttpClient("CustomerCraterApi",client=>client.BaseAddress=new Uri("https://localhost:7255/"))
            .ConfigurePrimaryHttpMessageHandler(()=>transport);
        builder.Services.AddHttpClient<ProductApiService>(client => client.BaseAddress = new Uri("https://localhost:7255/"))
            .ConfigurePrimaryHttpMessageHandler(() => transport);
        await using var app=builder.Build();
        app.UseAuthentication();
        app.Use((context,next)=>{
            if(context.Request.Headers["X-Fixture"].ToString() is "customer" or "operator")
                context.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("fixture-kind",context.Request.Headers["X-Fixture"].ToString())],"fixture"));
            return next(context);
        });
        app.UseAuthorization(); app.UseAntiforgery(); app.MapCustomerCrater();
        await app.StartAsync();
        try
        {
            using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){BaseAddress=new Uri(app.Urls.Single())};
            async Task<HttpResponseMessage> Request(string suffix="",string? kind="customer",bool post=false,HttpContent? content=null,string? cookie=null,int productId=139){
                using var request=new HttpRequestMessage(post?HttpMethod.Post:HttpMethod.Get,$"/customer/crater/{productId}"+suffix);
                if(kind is not null)request.Headers.Add("X-Fixture",kind);
                if(cookie is not null)request.Headers.Add("Cookie",cookie);
                request.Content=content;return await http.SendAsync(request);
            }
            check((await Request(kind:null)).StatusCode==HttpStatusCode.Unauthorized,"Anonymous Web customer generation page denied");
            check((await Request(kind:"operator")).StatusCode==HttpStatusCode.Forbidden,"Operator-only Web principal denied");
            builder.Configuration["CustomerCrater:Enabled"]=null;
            var disabled=await Request();
            check((await disabled.Content.ReadAsStringAsync()).Contains("generation is disabled")&&transport.Calls==0,"Disabled Web gate performs no token/API call");
            builder.Configuration["CustomerCrater:Enabled"]="true";
            var ready=await Request();var html=await ready.Content.ReadAsStringAsync();
            check(html.Contains("Generate one image")&&!html.Contains("<button disabled>"),"Ready form is based on server eligibility");
            check(ready.Headers.CacheControl?.NoStore==true,"Private customer page is not cached");
            check((await Request("/attempt",post:true)).StatusCode==HttpStatusCode.BadRequest&&transport.Posts==0,"Customer generation POST requires antiforgery");
            var field=Regex.Match(html,"name='([^']+)' value='([^']+)'");
            var cookie=ready.Headers.GetValues("Set-Cookie").First(v=>v.Contains("Antiforgery")).Split(';')[0];
            var posted=await Request("/attempt",post:true,cookie:cookie,content:new FormUrlEncodedContent(new Dictionary<string,string>{[field.Groups[1].Value]=WebUtility.HtmlDecode(field.Groups[2].Value)}));
            check(posted.StatusCode==HttpStatusCode.Redirect&&posted.Headers.Location?.OriginalString=="/customer/crater/139"&&transport.Posts==1,"One protected Web POST redirects to read-only status");
            transport.State="Completed";transport.CanGenerate=false;transport.Image=true;
            check((await (await Request()).Content.ReadAsStringAsync()).Contains("/customer/crater/139/image"),"Completed page uses protected private preview");
            transport.PreviewFails=true;
            check((await Request("/image")).StatusCode==HttpStatusCode.NotFound,"Failed Web preview is safe");
            await Request("/image");await Request();
            check(transport.Posts==1,"Preview/status refreshes never POST generation");
            foreach(var state in new[]{"Generating","Failed","Unknown"}){
                transport.State=state;transport.Image=false;
                var page=await (await Request()).Content.ReadAsStringAsync();
                check(page.Contains(state)&&!page.Contains("Generate one image"),"Web state disallows generation: "+state);
            }
              transport.State = "DailyLimitReached";
              var quotaPage = await (await Request()).Content.ReadAsStringAsync();
              check(quotaPage.Contains("shared daily generation limit") && !quotaPage.Contains("Generate one image"),
                  "Web explains the shared daily cap and hides the generation form");
            transport.State = "Completed";
            transport.Image = true;
            transport.CanGenerate = true;
            var retest = await Request();
            var retestHtml = await retest.Content.ReadAsStringAsync();
            var caption = retestHtml.IndexOf("Your private Crater It preview", StringComparison.Ordinal);
            var retestAction = retestHtml.IndexOf("action='/customer/crater/139/attempt?repeat=true'", StringComparison.Ordinal);
            check(caption >= 0 && retestAction > caption, "The eligible retest action appears below the private preview");
            var retestField = Regex.Match(retestHtml, "name='([^']+)' value='([^']+)'");
            var retestCookie = retest.Headers.GetValues("Set-Cookie").First(v => v.Contains("Antiforgery")).Split(';')[0];
            using var retestForm = new FormUrlEncodedContent(new Dictionary<string, string>
                { [retestField.Groups[1].Value] = WebUtility.HtmlDecode(retestField.Groups[2].Value) });
            var retestPosted = await Request("/attempt?repeat=true", post: true, cookie: retestCookie, content: retestForm);
            check(retestPosted.StatusCode == HttpStatusCode.Redirect && transport.LastPostQuery == "?repeat=true",
                "Website forwards explicit retest intent after antiforgery validation");
            var other = await (await Request(productId: 140)).Content.ReadAsStringAsync();
            check(other.Contains("/customer/crater/140/image") && other.Contains("/products/140?crater=true") &&
                transport.LastPath == "/api/customers/crater/140/attempt", "Website forwards the selected catalogue product");
            transport.CanClassic = true;
            var classicPage = await Request(productId: 140);
            var classicHtml = await classicPage.Content.ReadAsStringAsync();
            var classicIndex = classicHtml.IndexOf(">Classic Crater</button>", StringComparison.Ordinal);
            var craterIndex = classicHtml.IndexOf(">Crater It</button>", StringComparison.Ordinal);
            check(classicIndex >= 0 && classicIndex < craterIndex, "Classic Crater appears above Crater It");
            check(!classicHtml.Contains("Classic Crater complete"), "An unpublished product shows no Classic completion");
            transport.PublishedClassic = true;
            const string jpeg = "https://localhost:7255/api/products/100/classic-crater-image?v=0";
            var published = await (await Request("?classic=published", productId: 100)).Content.ReadAsStringAsync();
            var summaryIndex = published.IndexOf("Oats &amp; Honey", StringComparison.Ordinal);
            check(summaryIndex > published.IndexOf("Local development. Generation", StringComparison.Ordinal) &&
                summaryIndex < published.IndexOf("Classic Crater</button>", StringComparison.Ordinal) &&
                published.Contains("<strong>Pack size:</strong> 500 g") && published.Contains("<strong>Brand:</strong> Fixture Brand"),
                "The selected product's encoded details appear under the development notice and above the controls");
            check(published.Contains("aria-label=\"Main navigation\"") &&
                published.Contains("href=\"previous\"") && published.Contains("href=\"products\"") &&
                published.Contains("aria-label=\"Mobile navigation\"") &&
                published.Contains("href=\"/customer/account?returnUrl=%2Fcustomer%2Fcrater%2F100\""),
                "Customer Crater uses the shared navigation and the account link preserves the selected product");
            check(published.Contains($">Classic Crater</button><div") &&
                published.Contains($"href=\"{jpeg}\"") && published.Contains($"src=\"{jpeg}\"") &&
                published.Contains("Classic Crater complete"), "The Classic control shows a thumbnail linked to the full JPEG");
            check(published.Contains("this.nextElementSibling.showModal()") &&
                published.Contains("<dialog ") &&
                published.Contains("type=\"button\" class=\"btn-close p-3\" aria-label=\"Close image preview\"") &&
                published.Contains("this.closest('dialog').close()"),
                "The full-size Classic preview opens and closes on the same page without submitting the form");
            transport.StatusUnavailable = true;
            var refreshed = await (await Request(productId: 100)).Content.ReadAsStringAsync();
            check(refreshed.Contains("Classic Crater complete") && refreshed.Contains($"src=\"{jpeg}\""),
                "Classic artwork survives refresh when private attempt status is unavailable");
            transport.StatusUnavailable = false;
            var classicField = Regex.Match(classicHtml, "name='([^']+)' value='([^']+)'");
            var classicCookie = classicPage.Headers.GetValues("Set-Cookie").First(v=>v.Contains("Antiforgery")).Split(';')[0];
            using var classicForm = new FormUrlEncodedContent(new Dictionary<string, string>
                { [classicField.Groups[1].Value] = WebUtility.HtmlDecode(classicField.Groups[2].Value) });
            var classicPosted = await Request("/classic", post: true, cookie: classicCookie, content: classicForm, productId: 140);
            check(classicPosted.Headers.Location?.OriginalString == "/customer/crater/140?classic=published" &&
                transport.LastPath == "/api/customers/crater/140/classic", "Classic generation posts for the selected product");
            transport.AttemptVersion = 14;
            var versioned = await (await Request(productId: 140)).Content.ReadAsStringAsync();
            check(versioned.Contains("attempt?repeat=true&amp;version=14"),
                "The unlimited form includes the version of its preview");
            using var versionedForm = new FormUrlEncodedContent(new Dictionary<string, string>
                { [classicField.Groups[1].Value] = WebUtility.HtmlDecode(classicField.Groups[2].Value) });
            var versionedPost = await Request("/attempt?repeat=true&version=14", post: true,
                cookie: classicCookie, content: versionedForm, productId: 140);
            check(versionedPost.StatusCode == HttpStatusCode.Redirect && transport.LastPostQuery == "?repeat=true&version=14",
                "Website forwards the version of the requested attempt");
            app.Environment.EnvironmentName="Production";
            var before=transport.Calls;await Request();
            check(transport.Calls==before,"Production Web gate performs no API call");
        }
        finally{await app.StopAsync();}
    }
}
public class WebTokens:DispatchProxy
{
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>
        method?.Name=="GetAccessTokenForUserAsync"?Task.FromResult("offline-customer-token"):throw new InvalidOperationException();
}
sealed class WebTransport:HttpMessageHandler
{
    public int Calls,Posts;public string State="Ready";public bool CanGenerate=true,Image,PreviewFails,CanClassic,PublishedClassic,StatusUnavailable;
    public string? LastPostQuery, LastPath;
    public int? AttemptVersion;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if (request.RequestUri?.AbsolutePath == "/api/products/100")
        {
            if (request.Method != HttpMethod.Get || request.Headers.Authorization is not null)
                throw new Exception("Public product details must be read without customer credentials");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new FlyerMonkey.Shared.Model.Product
                    { ID = 100, Name = "Oats & Honey", PackSizeText = "500 g", Brand = "Fixture Brand" })
            });
        }
        if (request.RequestUri?.AbsolutePath.StartsWith("/api/products/") == true &&
            !request.RequestUri.AbsolutePath.EndsWith("/classic-crater-image"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        if (request.RequestUri?.AbsolutePath.EndsWith("/classic-crater-image") == true)
        {
            if (request.Method != HttpMethod.Get || request.Headers.Authorization is not null)
                throw new Exception("Public artwork must be read without customer credentials");
            Calls++;
            if (!PublishedClassic || request.RequestUri.AbsolutePath != "/api/products/100/classic-crater-image")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var image = new ByteArrayContent([255, 216, 255]);
            image.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = image });
        }
        if(request.RequestUri?.Scheme!="https"||request.RequestUri.Host!="localhost"||request.Headers.Authorization?.Parameter!="offline-customer-token")
            throw new Exception("Unsafe customer forwarding");
        Calls++;
        LastPath = request.RequestUri.AbsolutePath;
        if (request.Method == HttpMethod.Post)
        {
            Posts++;
            LastPostQuery = request.RequestUri.Query;
        }
        if(request.RequestUri.AbsolutePath.EndsWith("/image"))return Task.FromResult(new HttpResponseMessage(PreviewFails?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK));
        if (StatusUnavailable) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new FlyerMonkey.Shared.Model.CustomerCraterAttempt(State,CanGenerate,Image,CanClassic,AttemptVersion))});
    }
}

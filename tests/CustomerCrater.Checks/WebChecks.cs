using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
        builder.Services.AddAuthentication("Fixture").AddCookie("Fixture",options=>{
            options.Events.OnRedirectToLogin=context=>{context.Response.StatusCode=401;return Task.CompletedTask;};
            options.Events.OnRedirectToAccessDenied=context=>{context.Response.StatusCode=403;return Task.CompletedTask;};
        });
        builder.Services.AddAuthorization(options=>options.AddPolicy(CustomerAuthentication.Policy,
            policy=>policy.RequireAuthenticatedUser().RequireClaim("fixture-kind","customer")));
        builder.Services.AddSingleton(DispatchProxy.Create<ITokenAcquisition,WebTokens>());
        var transport=new WebTransport();
        builder.Services.AddHttpClient("CustomerCraterApi",client=>client.BaseAddress=new Uri("https://localhost:7255/"))
            .ConfigurePrimaryHttpMessageHandler(()=>transport);
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
            async Task<HttpResponseMessage> Request(string suffix="",string? kind="customer",bool post=false,HttpContent? content=null,string? cookie=null){
                using var request=new HttpRequestMessage(post?HttpMethod.Post:HttpMethod.Get,"/customer/crater/139"+suffix);
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
    public int Calls,Posts;public string State="Ready";public bool CanGenerate=true,Image,PreviewFails;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        if(request.RequestUri?.Scheme!="https"||request.RequestUri.Host!="localhost"||request.Headers.Authorization?.Parameter!="offline-customer-token")
            throw new Exception("Unsafe customer forwarding");
        Calls++;if(request.Method==HttpMethod.Post)Posts++;
        if(request.RequestUri.AbsolutePath.EndsWith("/image"))return Task.FromResult(new HttpResponseMessage(PreviewFails?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new FlyerMonkey.Shared.Model.CustomerCraterAttempt(State,CanGenerate,Image))});
    }
}

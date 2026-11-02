using System.Security.Claims;
using AspNet.Security.OAuth.GitHub;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.GitHub;
using Djohnnie.SoftwareFactory.Web.Auth;
using Djohnnie.SoftwareFactory.Web.Components;
using Djohnnie.SoftwareFactory.Web.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices(options =>
    options.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Outlined);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<MemoryCacheTicketStore>();
builder.Services.AddScoped<RepositoryContext>();
builder.Services.AddScoped<IGitHubTokenProvider, GitHubTokenProvider>();
builder.Services.AddSoftwareFactory(builder.Configuration);

builder.Services.AddOptions<GitHubAuthOptions>()
    .Bind(builder.Configuration.GetSection(GitHubAuthOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.AllowedLogin), "GitHub:AllowedLogin must be configured.")
    .ValidateOnStart();

var gitHub = builder.Configuration.GetSection(GitHubAuthOptions.SectionName).Get<GitHubAuthOptions>() ?? new();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "dghsf.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddGitHub(options =>
    {
        options.ClientId = gitHub.ClientId;
        options.ClientSecret = gitHub.ClientSecret;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.CallbackPath = "/signin-github";
        options.Scope.Add("read:user");
        options.Scope.Add("repo");
        options.SaveTokens = true;
        options.Events = new OAuthEvents
        {
            OnCreatingTicket = context =>
            {
                var allowed = context.HttpContext.RequestServices.GetRequiredService<IOptions<GitHubAuthOptions>>().Value;
                var login = context.Identity?.FindFirst(ClaimTypes.Name)?.Value;

                if (!string.Equals(login, allowed.AllowedLogin, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException($"GitHub user '{login}' is not allowed.");
                }

                if (!string.IsNullOrEmpty(context.AccessToken))
                {
                    context.Identity!.AddClaim(new Claim(GitHubClaims.AccessToken, context.AccessToken));
                }

                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                context.Response.Redirect("/login?error=denied");
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    });

// The ticket (including the GitHub access token) stays server-side; the cookie only holds a session key.
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<MemoryCacheTicketStore>((options, store) => options.SessionStore = store);

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateApplicationDatabaseAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();

app.MapGet("/auth/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = IsLocal(returnUrl) ? returnUrl : "/" },
        [GitHubAuthenticationDefaults.AuthenticationScheme]))
    .AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsLocal(string? url) =>
    !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

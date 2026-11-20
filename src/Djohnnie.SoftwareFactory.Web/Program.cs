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
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices(options =>
    options.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Outlined);
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<SqlTicketStore>();
builder.Services.AddScoped<RepositoryContext>();
builder.Services.AddScoped<IGitHubTokenProvider, GitHubTokenProvider>();
builder.Services.AddSingleton<GitHubSessionValidator>();
builder.Services.AddHttpClient(GitHubSessionValidator.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DGHSF/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.Timeout = TimeSpan.FromSeconds(10);
});
// Replaces the default circuit authentication state: it signs the circuit out when the GitHub token stops working.
builder.Services.AddScoped<AuthenticationStateProvider, GitHubAuthenticationStateProvider>();
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
        options.ExpireTimeSpan = SqlTicketStore.Lifetime;
        // An expired or revoked GitHub token ends the session, so the next request goes to the login page.
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                await context.HttpContext.RequestServices.GetRequiredService<GitHubSessionValidator>().ValidatePrincipalAsync(context);
            }
        };
    })
    .AddGitHub(options =>
    {
        options.ClientId = gitHub.ClientId;
        options.ClientSecret = gitHub.ClientSecret;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.CallbackPath = "/signin-github";
        // "user" (which includes read:user): GitHub's personal billing usage endpoints accept no narrower scope and answer 404 without it.
        options.Scope.Add("user");
        options.Scope.Add("repo");
        // Copilot usage metrics of the organizations the user owns (overview page).
        options.Scope.Add("read:org");
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

// The ticket (including the GitHub access token) stays server-side in SQL (encrypted); the cookie only holds a session key.
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<SqlTicketStore>((options, store) => options.SessionStore = store);

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.MigrateApplicationDatabaseAsync();
}

try
{
    await app.Services.SeedApplicationDataAsync();
}
catch (Exception ex)
{
    // Not fatal: the internal agents fall back to their default prompts and are seeded when their page is opened.
    app.Logger.LogWarning(ex, "Could not seed the internal agents.");
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

// An open circuit whose GitHub token stopped working lands here: the cookie session is ended before signing in again.
app.MapGet("/auth/session-expired", async (HttpContext context, string? returnUrl) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect($"/login?error=expired&returnUrl={Uri.EscapeDataString(IsLocal(returnUrl) ? returnUrl! : "/")}");
})
    .AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsLocal(string? url)
{
    return !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
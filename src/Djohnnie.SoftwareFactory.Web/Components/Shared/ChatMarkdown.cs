using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// Renders the Markdown of agent replies and GitHub comments as safe HTML. Model output can be steered by prompt injection
/// (e.g. from a repository file) and anyone who can comment writes GitHub comments, so both are treated as untrusted: raw HTML is escaped, only http(s) and mailto links are kept, links
/// open in a new tab without a referrer, and images become links, so a reply can't make the browser load a URL.
/// With app links allowed, a relative link to the page of a work item or mindmap (see <see cref="Contracts.AppPaths"/>)
/// is kept too and opens in the app itself; every other relative link is dropped. A link (or bare URL) that
/// <c>appLinkFor</c> knows, such as the GitHub issue of a work item, is turned into a link to that page.
/// </summary>
public static class ChatMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .Build();

    /// <param name="markdown">The untrusted Markdown.</param>
    /// <param name="allowAppLinks">Keeps links to the pages of work items and mindmaps, for the replies of the DGHSF Agent.</param>
    /// <param name="appLinkFor">With <paramref name="allowAppLinks"/>: the page in DGHSF for a URL, or <c>null</c> to keep the URL.</param>
    public static string ToHtml(string markdown, bool allowAppLinks = false, Func<string, string?>? appLinkFor = null)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (allowAppLinks && AppLinkFor(link.GetDynamicUrl?.Invoke() ?? link.Url, appLinkFor) is { } page)
            {
                link.GetDynamicUrl = null;
                link.Url = page;
            }
            MakeSafe(link, allowAppLinks);
        }
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (allowAppLinks && !autolink.IsEmail && AppLinkFor(autolink.Url, appLinkFor) is { } page)
            {
                var link = new LinkInline(page, string.Empty);
                link.AppendChild(new LiteralInline(autolink.Url));
                link.GetAttributes().AddClass("app-link");
                autolink.ReplaceBy(link);
            }
            else if (IsSafe(autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url))
            {
                AddLinkAttributes(autolink);
            }
            else
            {
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
            }
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        return writer.ToString();
    }

    private static void MakeSafe(LinkInline link, bool allowAppLinks)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
        var isAppLink = allowAppLinks && Contracts.AppPaths.IsLinkablePage(url);
        if (!isAppLink && !IsSafe(url))
        {
            // Keep the text, drop the link.
            link.ReplaceBy(new LiteralInline(PlainText(link) is { Length: > 0 } text ? text : url ?? string.Empty));
            return;
        }

        if (link.IsImage)
        {
            link.IsImage = false;
            if (link.FirstChild is null)
            {
                link.AppendChild(new LiteralInline(url!));
            }
        }
        if (isAppLink)
        {
            // Same tab, so the router navigates within the app.
            link.GetAttributes().AddClass("app-link");
            return;
        }
        AddLinkAttributes(link);
    }

    private static string? AppLinkFor(string? url, Func<string, string?>? appLinkFor)
    {
        return url is null || appLinkFor?.Invoke(url) is not { } page || !Contracts.AppPaths.IsLinkablePage(page) ? null : page;
    }

    private static void AddLinkAttributes(Inline link)
    {
        var attributes = link.GetAttributes();
        attributes.AddPropertyIfNotExist("target", "_blank");
        attributes.AddPropertyIfNotExist("rel", "noopener noreferrer nofollow");
    }

    private static bool IsSafe(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto);
    }

    private static string PlainText(ContainerInline container)
    {
        return string.Concat(container.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));
    }
}

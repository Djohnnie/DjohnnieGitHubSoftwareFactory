using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// Renders the Markdown of agent replies and GitHub comments as safe HTML. Model output can be steered by prompt injection
/// (e.g. from a repository file) and anyone who can comment writes GitHub comments, so both are treated as untrusted: raw HTML is escaped, only http(s) and mailto links are kept, links
/// open in a new tab without a referrer, and images become links, so a reply can't make the browser load a URL. Only images
/// that <c>showImage</c> trusts, such as the screenshots in DGHSF's own image storage, are shown, linked to the full image.
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
    /// <param name="showImage">Whether an image URL may be shown inline; every other image becomes a link.</param>
    public static string ToHtml(string markdown, bool allowAppLinks = false, Func<string, string?>? appLinkFor = null, Func<string, bool>? showImage = null)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (allowAppLinks && AppLinkFor(link.GetDynamicUrl?.Invoke() ?? link.Url, appLinkFor) is { } page)
            {
                link.GetDynamicUrl = null;
                link.Url = page;
            }
            MakeSafe(link, allowAppLinks, showImage);
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
        return AllowSubscript(writer.ToString());
    }

    /// <summary>
    /// Turns an escaped <c>&lt;sub&gt;…&lt;/sub&gt;</c> pair on one line back into a subscript, as GitHub shows it (the DGHSF
    /// agent writes its header and footer that way). The tags have no attributes and the text between them is already safe
    /// HTML, so this adds no markup of its own; a pair in code, or one whose text opens or closes a tag it doesn't close or
    /// open (e.g. across paragraphs or table cells), stays escaped.
    /// </summary>
    private static string AllowSubscript(string html)
    {
        return Subscript.Replace(html, match =>
        {
            if (match.Groups["code"].Success || !IsBalanced(match.Groups["text"].Value))
            {
                return match.Value;
            }
            return $"<sub>{match.Groups["text"].Value}</sub>";
        });
    }

    private static bool IsBalanced(string html)
    {
        var open = new Stack<string>();
        foreach (System.Text.RegularExpressions.Match tag in Tag.Matches(html))
        {
            var name = tag.Groups["name"].Value.ToLowerInvariant();
            if (tag.Groups["close"].Success)
            {
                if (!open.TryPop(out var opened) || opened != name)
                {
                    return false;
                }
            }
            else if (!tag.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                open.Push(name);
            }
        }
        return open.Count == 0;
    }

    private static readonly System.Text.RegularExpressions.Regex Subscript = new(
        @"(?<code><pre[\s\S]*?</pre>|<code[^>]*>[\s\S]*?</code>)|&lt;sub&gt;(?<text>(?:(?!&lt;/?sub&gt;)[^\n])*?)&lt;/sub&gt;",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex Tag = new(
        @"<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9]*)[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void MakeSafe(LinkInline link, bool allowAppLinks, Func<string, bool>? showImage)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
        var isAppLink = allowAppLinks && Contracts.AppPaths.IsLinkablePage(url);
        if (!isAppLink && !IsSafe(url))
        {
            // Keep the text, drop the link.
            link.ReplaceBy(new LiteralInline(PlainText(link) is { Length: > 0 } text ? text : url ?? string.Empty));
            return;
        }

        if (link.IsImage && !isAppLink && showImage?.Invoke(url!) == true)
        {
            ShowImage(link, url!);
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

    /// <summary>Keeps the image, lazily loaded without a referrer, and links it to the full image unless it already is a link.</summary>
    private static void ShowImage(LinkInline image, string url)
    {
        var attributes = image.GetAttributes();
        attributes.AddPropertyIfNotExist("loading", "lazy");
        attributes.AddPropertyIfNotExist("referrerpolicy", "no-referrer");
        // FirstParentOfType starts at the image itself, which is a LinkInline too.
        if (image.Parent is { } parent && parent.FirstParentOfType<LinkInline>() is null)
        {
            var link = new LinkInline(url, string.Empty);
            image.ReplaceBy(link, copyChildren: false);
            link.AppendChild(image);
            AddLinkAttributes(link);
        }
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

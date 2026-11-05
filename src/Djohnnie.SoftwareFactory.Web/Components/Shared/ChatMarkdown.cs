using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// Renders the Markdown of Copilot replies as safe HTML. Model output can be steered by prompt injection (e.g. from a
/// repository file), so it is treated as untrusted: raw HTML is escaped, only http(s) and mailto links are kept, links
/// open in a new tab without a referrer, and images become links, so a reply can't make the browser load a URL.
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

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            MakeSafe(link);
        }
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (IsSafe(autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url))
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

    private static void MakeSafe(LinkInline link)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
        if (!IsSafe(url))
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
        AddLinkAttributes(link);
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

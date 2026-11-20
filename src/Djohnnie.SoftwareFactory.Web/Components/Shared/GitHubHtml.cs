using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Ganss.Xss;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// Keeps the HTML that GitHub renders in comments and that is safe here, and removes the rest, for
/// <see cref="ChatMarkdown.ToHtml"/> with HTML allowed. Copilot code review, for example, writes its overview with collapsible
/// <c>&lt;details&gt;</c> sections and severity badges as images.
/// </summary>
/// <remarks>
/// The same rules as for Markdown apply. Only an allowlist of tags and attributes is kept, without styles, ids or event
/// handlers. Links must be absolute http(s) or mailto, and open in a new tab without a referrer. The browser must not load
/// a URL from a comment, so an image is only shown when <c>showImage</c> trusts it. Any other image becomes its alt text (a
/// badge such as "Medium severity") or, without alt text, a link to it. Tags that aren't allowed are removed but their text
/// stays, as on GitHub; scripts, styles and other embedded content are removed completely.
/// </remarks>
public static partial class GitHubHtml
{
    private static readonly string[] Tags =
    [
        // What Markdown renders.
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "code", "em", "strong", "del", "a", "img", "br", "hr",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "input",
        // The other HTML GitHub allows in comments.
        "details", "summary", "div", "span", "b", "i", "u", "s", "strike", "ins", "mark", "kbd", "samp", "var", "tt", "sub", "sup", "small",
        "big", "abbr", "cite", "q", "dl", "dt", "dd", "caption", "rp", "rt", "ruby"
    ];

    private static readonly string[] Attributes =
    [
        "href", "src", "alt", "title", "align", "valign", "width", "height", "open", "colspan", "rowspan", "start", "type", "checked", "disabled",
        "class", "target", "rel", "loading", "referrerpolicy"
    ];

    /// <summary>The classes Markdown adds (task lists) and app links; GitHub comments can't use the app's own classes.</summary>
    private static readonly string[] Classes = ["contains-task-list", "task-list-item", "task-list-item-checkbox", "app-link"];

    /// <summary>Content that is never shown as text when its tag is removed.</summary>
    private static readonly string[] Dropped = ["script", "style", "noscript", "template", "iframe", "object", "embed", "textarea", "select", "svg", "math", "title"];

    /// <param name="html">HTML from Markdown with raw HTML in it.</param>
    /// <param name="showImage">Whether an image URL may be shown inline.</param>
    public static string Sanitize(string html, Func<string, bool>? showImage)
    {
        var sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(Attributes, StringComparer.OrdinalIgnoreCase),
            AllowedClasses = new HashSet<string>(Classes, StringComparer.Ordinal),
            AllowedSchemes = new HashSet<string>(["http", "https", "mailto"], StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href", "src"], StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>()
        })
        {
            KeepChildNodes = true
        };
        sanitizer.RemovingTag += (_, e) =>
        {
            if (Dropped.Contains(e.Tag.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                e.Tag.TextContent = string.Empty;
            }
        };
        sanitizer.PostProcessNode += (_, e) =>
        {
            switch (e.Node)
            {
                case IHtmlAnchorElement anchor:
                    MakeSafe(anchor);
                    break;
                case IHtmlImageElement image when ReplacementOf(image, e.Document, showImage) is { } replacement:
                    e.ReplacementNodes.Add(replacement);
                    break;
                case IHtmlInputElement input:
                    // Only the checkboxes of task lists, and they can't be ticked.
                    if (!string.Equals(input.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
                    {
                        e.ReplacementNodes.Add(e.Document.CreateTextNode(string.Empty));
                    }
                    else
                    {
                        input.SetAttribute("disabled", "disabled");
                    }
                    break;
            }
        };
        return sanitizer.Sanitize(html);
    }

    private static void MakeSafe(IHtmlAnchorElement anchor)
    {
        if (anchor.GetAttribute("href") is not { } href)
        {
            return;
        }
        if (!ChatMarkdown.IsSafe(href) && !(anchor.ClassList.Contains("app-link") && Contracts.AppPaths.IsLinkablePage(href)))
        {
            // Keep the text, drop the link.
            anchor.RemoveAttribute("href");
            anchor.RemoveAttribute("target");
            anchor.RemoveAttribute("rel");
            return;
        }
        if (!anchor.ClassList.Contains("app-link"))
        {
            anchor.SetAttribute("target", "_blank");
            anchor.SetAttribute("rel", "noopener noreferrer nofollow");
        }
    }

    /// <summary>
    /// <c>null</c> to show a trusted image (lazily, without a referrer); otherwise what replaces it: its alt text as a badge
    /// (colored for Copilot's severities), or a link to it.
    /// </summary>
    private static INode? ReplacementOf(IHtmlImageElement image, IDocument document, Func<string, bool>? showImage)
    {
        var src = image.GetAttribute("src");
        if (ChatMarkdown.IsSafe(src) && showImage?.Invoke(src!) == true)
        {
            image.SetAttribute("loading", "lazy");
            image.SetAttribute("referrerpolicy", "no-referrer");
            return null;
        }

        var alt = image.GetAttribute("alt")?.Trim();
        if (!string.IsNullOrEmpty(alt))
        {
            var badge = document.CreateElement("span");
            badge.ClassName = Severity().Match(alt) is { Success: true } severity
                ? $"md-image-alt md-severity-{severity.Groups["level"].Value.ToLowerInvariant()}"
                : "md-image-alt";
            badge.TextContent = alt;
            return badge;
        }
        if (!ChatMarkdown.IsSafe(src) || image.ParentElement is IHtmlAnchorElement)
        {
            return document.CreateTextNode(string.Empty);
        }
        var link = document.CreateElement("a");
        link.SetAttribute("href", src!);
        link.SetAttribute("target", "_blank");
        link.SetAttribute("rel", "noopener noreferrer nofollow");
        link.TextContent = "image";
        return link;
    }

    [GeneratedRegex(@"^(?<level>critical|high|medium|low)\b.*severity$", RegexOptions.IgnoreCase)]
    private static partial Regex Severity();
}

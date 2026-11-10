using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Djohnnie.SoftwareFactory.Agents;

/// <summary>A fetched web page as text.</summary>
/// <param name="Url">The URL that answered, after redirects.</param>
/// <param name="Title">The page title, if any.</param>
/// <param name="Text">The readable text: Markdown-like for HTML, as is for other text.</param>
/// <param name="IsTruncated"><c>true</c> when the page was longer than the limit and was cut off.</param>
public sealed record WebPage(Uri Url, string? Title, string Text, bool IsTruncated);

/// <summary>Thrown when a page can't be fetched; the message can be shown to the model.</summary>
public sealed class WebFetchException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Fetches public web pages and turns them into readable text. Only public addresses can be reached: the check runs on
/// the address that is actually connected to, so redirects and DNS names that point to an internal address (such as the
/// Azure metadata endpoint or localhost) are refused too.
/// </summary>
public sealed partial class WebPageFetcher
{
    private const int MaxRedirects = 5;

    private static readonly HashSet<string> RemovedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "canvas", "iframe", "object", "embed",
        "nav", "footer", "aside", "form", "button", "select", "dialog"
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "main", "header", "ul", "ol", "dl", "dt", "dd", "table", "thead", "tbody",
        "tr", "blockquote", "figure", "figcaption", "details", "summary", "hr", "address"
    };

    private readonly HttpClient _httpClient;
    private readonly WebFetchOptions _options;

    public WebPageFetcher(WebFetchOptions options)
        : this(options, CreateHandler())
    {
    }

    /// <param name="options">The limits.</param>
    /// <param name="handler">The handler that sends the requests; tests replace it, production uses <see cref="CreateHandler"/>.</param>
    public WebPageFetcher(WebFetchOptions options, HttpMessageHandler handler)
    {
        _options = options;
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DGHSF/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,text/markdown,text/plain;q=0.9,application/json;q=0.8,*/*;q=0.1");
    }

    /// <summary>A handler that only connects to public addresses and doesn't use a proxy, which would hide the real address.</summary>
    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRedirects,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectToPublicAddressAsync
        };
    }

    /// <exception cref="WebFetchException">The URL isn't a public http(s) URL, or the page can't be read.</exception>
    public async Task<WebPage> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (!IsWebUrl(url))
        {
            throw new WebFetchException("Only absolute http and https URLs can be fetched.");
        }

        // Also covers reading the body, which HttpClient.Timeout doesn't with ResponseHeadersRead.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(url) { Fragment = string.Empty }.Uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var finalUrl = response.RequestMessage?.RequestUri ?? url;
            if (!response.IsSuccessStatusCode)
            {
                throw new WebFetchException($"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "text/html";
            if (!IsTextMediaType(mediaType))
            {
                throw new WebFetchException($"The URL returns {mediaType}, which can't be read as text.");
            }

            var body = await ReadBodyAsync(response.Content, timeout.Token);
            var (title, text) = IsHtml(mediaType) ? HtmlToText(body, finalUrl) : (null, body.Trim());
            var isTruncated = text.Length > _options.MaxCharacters;
            return new WebPage(finalUrl, title, isTruncated ? text[.._options.MaxCharacters] : text, isTruncated);
        }
        catch (WebFetchException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WebFetchException("The page took too long to answer.");
        }
        catch (HttpRequestException ex)
        {
            throw new WebFetchException(ex.InnerException is WebFetchException blocked ? blocked.Message : $"The page could not be fetched: {ex.Message}", ex);
        }
    }

    public static bool IsWebUrl(Uri url)
    {
        return url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(url.UserInfo);
    }

    /// <summary>
    /// <c>true</c> for addresses that aren't on the public internet: loopback, private, link-local (including
    /// 169.254.169.254), carrier-grade NAT, multicast and reserved ranges, also when wrapped in an IPv6 address.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 198 && b[1] is 18 or 19)
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (IsNat64(bytes) || IsIPv4Compatible(bytes))
        {
            return IsBlockedAddress(new IPAddress(bytes[12..]));
        }

        return IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast
            || address.IsIPv6UniqueLocal
            || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }

    /// <summary>Turns an HTML page into Markdown-like text: headings, paragraphs, lists, links and code blocks.</summary>
    public static (string? Title, string Text) HtmlToText(string html, Uri baseUrl)
    {
        var document = new HtmlParser().ParseDocument(html);
        var title = document.Title?.Trim();
        foreach (var element in document.All.Where(e => RemovedElements.Contains(e.LocalName)).ToList())
        {
            element.Remove();
        }

        var root = (INode?)document.QuerySelector("main") ?? document.QuerySelector("article") ?? document.Body ?? document.DocumentElement;
        var builder = new StringBuilder();
        AppendNode(root, builder, baseUrl);
        return (string.IsNullOrEmpty(title) ? null : title, Tidy(builder.ToString()));
    }

    private static async ValueTask<Socket> ConnectSocketAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(address => !IsBlockedAddress(address)).ToArray();
        if (allowed.Length == 0)
        {
            throw new WebFetchException($"{host} is not a public internet address, so it can't be fetched.");
        }

        var socket = await ConnectSocketAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static bool IsNat64(byte[] bytes)
    {
        return bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b && bytes[4..12].All(b => b == 0);
    }

    private static bool IsIPv4Compatible(byte[] bytes)
    {
        return bytes[..12].All(b => b == 0) && !(bytes[12] == 0 && bytes[13] == 0 && bytes[14] == 0 && bytes[15] <= 1);
    }

    private static bool IsHtml(string mediaType)
    {
        return mediaType is "text/html" or "application/xhtml+xml";
    }

    private static bool IsTextMediaType(string mediaType)
    {
        return mediaType.StartsWith("text/", StringComparison.Ordinal)
            || mediaType is "application/json" or "application/xml" or "application/xhtml+xml" or "application/rss+xml" or "application/atom+xml"
            || mediaType.EndsWith("+json", StringComparison.Ordinal);
    }

    private async Task<string> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[Math.Max(1, _options.MaxResponseBytes)];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
        }

        return EncodingFor(content.Headers.ContentType).GetString(buffer, 0, length);
    }

    private static Encoding EncodingFor(MediaTypeHeaderValue? contentType)
    {
        try
        {
            return string.IsNullOrWhiteSpace(contentType?.CharSet) ? Encoding.UTF8 : Encoding.GetEncoding(contentType.CharSet.Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static void AppendNode(INode node, StringBuilder builder, Uri baseUrl)
    {
        if (node is IText text)
        {
            builder.Append(Whitespace().Replace(text.Data, " "));
            return;
        }

        if (node is not IElement element)
        {
            AppendChildren(node, builder, baseUrl);
            return;
        }

        var name = element.LocalName.ToLowerInvariant();
        switch (name)
        {
            case "br":
                builder.Append('\n');
                break;
            case "pre":
                builder.Append("\n\n```\n").Append(element.TextContent.Trim('\n')).Append("\n```\n\n");
                break;
            case "code":
                builder.Append('`').Append(Whitespace().Replace(element.TextContent, " ")).Append('`');
                break;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                builder.Append("\n\n").Append('#', name[1] - '0').Append(' ');
                AppendChildren(element, builder, baseUrl);
                builder.Append("\n\n");
                break;
            case "li":
                // The next item or the end of the list starts a new line, so items stay without empty lines in between.
                builder.Append("\n- ");
                AppendChildren(element, builder, baseUrl);
                break;
            case "td" or "th":
                builder.Append(" | ");
                AppendChildren(element, builder, baseUrl);
                break;
            case "img":
                if (element.GetAttribute("alt") is { Length: > 0 } alt)
                {
                    builder.Append("[image: ").Append(alt.Trim()).Append(']');
                }
                break;
            case "a":
                AppendLink(element, builder, baseUrl);
                break;
            default:
                var isBlock = BlockElements.Contains(name);
                if (isBlock)
                {
                    builder.Append('\n');
                }
                AppendChildren(element, builder, baseUrl);
                if (isBlock)
                {
                    builder.Append('\n');
                }
                break;
        }
    }

    private static void AppendChildren(INode node, StringBuilder builder, Uri baseUrl)
    {
        foreach (var child in node.ChildNodes)
        {
            AppendNode(child, builder, baseUrl);
        }
    }

    private static void AppendLink(IElement element, StringBuilder builder, Uri baseUrl)
    {
        var label = new StringBuilder();
        AppendChildren(element, label, baseUrl);
        var text = Whitespace().Replace(label.ToString(), " ").Trim();
        var href = element.GetAttribute("href");
        if (text.Length > 0 && href is not null && Uri.TryCreate(baseUrl, href, out var target) && IsWebUrl(target))
        {
            builder.Append('[').Append(text).Append("](").Append(target.AbsoluteUri).Append(')');
        }
        else
        {
            builder.Append(text);
        }
    }

    /// <summary>Trims every line outside code blocks and leaves at most one empty line between blocks.</summary>
    private static string Tidy(string text)
    {
        var result = new StringBuilder();
        var blank = 0;
        var inCode = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var isFence = rawLine.Trim().StartsWith("```", StringComparison.Ordinal);
            var line = inCode && !isFence ? rawLine.TrimEnd() : rawLine.Trim();
            if (isFence)
            {
                inCode = !inCode;
            }

            if (line.Length == 0 && !inCode)
            {
                blank++;
                continue;
            }

            if (result.Length > 0)
            {
                result.Append(blank > 0 ? "\n\n" : "\n");
            }
            result.Append(line);
            blank = 0;
        }

        return result.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

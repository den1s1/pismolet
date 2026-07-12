using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Domain.Mailings;

public static class HtmlMessageLinkifier
{
    private static readonly Regex AbsoluteHttpUrlRegex = new(
        "(?<![\\p{L}\\p{N}_@:/])https?://[^\\s<>\"']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area",
        "base",
        "br",
        "col",
        "embed",
        "hr",
        "img",
        "input",
        "link",
        "meta",
        "param",
        "source",
        "track",
        "wbr"
    };

    private static readonly HashSet<string> SuppressedTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "script",
        "style",
        "textarea"
    };

    private static readonly string[] ServiceFooterMarkers =
    {
        "pismolet-footer",
        "service-preview-footer",
        "data-pismolet-service-footer"
    };

    private static readonly string[] TrailingEntities =
    {
        "&quot;",
        "&#34;",
        "&#39;",
        "&apos;",
        "&raquo;",
        "&rdquo;",
        "&rsquo;"
    };

    public static string Linkify(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html ?? string.Empty;
        }

        var result = new StringBuilder(html.Length + 64);
        var openElements = new List<ElementFrame>();
        var suppressedDepth = 0;
        var index = 0;

        while (index < html.Length)
        {
            var tagStart = html.IndexOf('<', index);
            if (tagStart < 0)
            {
                AppendText(result, html[index..], suppressedDepth == 0);
                break;
            }

            if (tagStart > index)
            {
                AppendText(result, html[index..tagStart], suppressedDepth == 0);
            }

            var tagEnd = FindTagEnd(html, tagStart);
            if (tagEnd < 0)
            {
                result.Append(html[tagStart..]);
                break;
            }

            var rawTag = html[tagStart..(tagEnd + 1)];
            result.Append(rawTag);
            UpdateElementStack(rawTag, openElements, ref suppressedDepth);
            index = tagEnd + 1;
        }

        return result.ToString();
    }

    private static void AppendText(StringBuilder result, string text, bool shouldLinkify)
    {
        result.Append(shouldLinkify ? LinkifyText(text) : text);
    }

    private static string LinkifyText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return AbsoluteHttpUrlRegex.Replace(text, match =>
        {
            var (encodedUrl, trailing) = SplitTrailingPunctuation(match.Value);
            if (string.IsNullOrWhiteSpace(encodedUrl))
            {
                return match.Value;
            }

            var originalUrl = WebUtility.HtmlDecode(encodedUrl);
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || IsTechnicalServiceUrl(uri))
            {
                return match.Value;
            }

            return $"<a href=\"{WebUtility.HtmlEncode(originalUrl)}\">{encodedUrl}</a>{trailing}";
        });
    }

    private static void UpdateElementStack(
        string rawTag,
        List<ElementFrame> openElements,
        ref int suppressedDepth)
    {
        if (rawTag.StartsWith("<!--", StringComparison.Ordinal)
            || rawTag.StartsWith("<!", StringComparison.Ordinal)
            || rawTag.StartsWith("<?", StringComparison.Ordinal))
        {
            return;
        }

        var tagName = ReadTagName(rawTag);
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return;
        }

        if (IsClosingTag(rawTag))
        {
            CloseElement(tagName, openElements, ref suppressedDepth);
            return;
        }

        if (VoidTags.Contains(tagName) || IsSelfClosingTag(rawTag))
        {
            return;
        }

        var suppressesText = SuppressedTextTags.Contains(tagName) || IsServiceFooterTag(rawTag);
        openElements.Add(new ElementFrame(tagName, suppressesText));
        if (suppressesText)
        {
            suppressedDepth++;
        }
    }

    private static void CloseElement(
        string tagName,
        List<ElementFrame> openElements,
        ref int suppressedDepth)
    {
        var matchingIndex = -1;
        for (var index = openElements.Count - 1; index >= 0; index--)
        {
            if (openElements[index].Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
            {
                matchingIndex = index;
                break;
            }
        }

        if (matchingIndex < 0)
        {
            return;
        }

        for (var index = openElements.Count - 1; index >= matchingIndex; index--)
        {
            if (openElements[index].SuppressesText)
            {
                suppressedDepth--;
            }

            openElements.RemoveAt(index);
        }
    }

    private static int FindTagEnd(string html, int tagStart)
    {
        if (html.AsSpan(tagStart).StartsWith("<!--", StringComparison.Ordinal))
        {
            var commentEnd = html.IndexOf("-->", tagStart + 4, StringComparison.Ordinal);
            return commentEnd < 0 ? -1 : commentEnd + 2;
        }

        var quote = '\0';
        for (var index = tagStart + 1; index < html.Length; index++)
        {
            var value = html[index];
            if (quote != '\0')
            {
                if (value == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (value is '\'' or '"')
            {
                quote = value;
                continue;
            }

            if (value == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static string ReadTagName(string rawTag)
    {
        var index = 1;
        while (index < rawTag.Length && char.IsWhiteSpace(rawTag[index]))
        {
            index++;
        }

        if (index < rawTag.Length && rawTag[index] == '/')
        {
            index++;
            while (index < rawTag.Length && char.IsWhiteSpace(rawTag[index]))
            {
                index++;
            }
        }

        var start = index;
        while (index < rawTag.Length
               && (char.IsLetterOrDigit(rawTag[index]) || rawTag[index] is '-' or ':'))
        {
            index++;
        }

        return index == start ? string.Empty : rawTag[start..index].ToLowerInvariant();
    }

    private static bool IsClosingTag(string rawTag)
    {
        var index = 1;
        while (index < rawTag.Length && char.IsWhiteSpace(rawTag[index]))
        {
            index++;
        }

        return index < rawTag.Length && rawTag[index] == '/';
    }

    private static bool IsSelfClosingTag(string rawTag)
    {
        var index = rawTag.Length - 2;
        while (index >= 0 && char.IsWhiteSpace(rawTag[index]))
        {
            index--;
        }

        return index >= 0 && rawTag[index] == '/';
    }

    private static bool IsServiceFooterTag(string rawTag) => ServiceFooterMarkers.Any(marker =>
        rawTag.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsTechnicalServiceUrl(Uri uri)
    {
        var path = uri.AbsolutePath.TrimEnd('/');
        return path.StartsWith("/unsubscribe", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/t/open", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/t/click", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Url, string Trailing) SplitTrailingPunctuation(string value)
    {
        var end = value.Length;
        while (end > 0)
        {
            if (TryRemoveTrailingEntity(value, ref end))
            {
                continue;
            }

            var current = value[end - 1];
            if (IsSimpleTrailingPunctuation(current))
            {
                end--;
                continue;
            }

            if (current is ')' or ']' or '}'
                && HasUnmatchedClosingBracket(value.AsSpan(0, end), current))
            {
                end--;
                continue;
            }

            break;
        }

        return (value[..end], value[end..]);
    }

    private static bool TryRemoveTrailingEntity(string value, ref int end)
    {
        foreach (var entity in TrailingEntities)
        {
            if (end >= entity.Length
                && value.AsSpan(end - entity.Length, entity.Length).Equals(entity, StringComparison.OrdinalIgnoreCase))
            {
                end -= entity.Length;
                return true;
            }
        }

        return false;
    }

    private static bool IsSimpleTrailingPunctuation(char value) => value is
        '.' or ',' or ';' or ':' or '!' or '?' or '…' or '。' or '»' or '”' or '’';

    private static bool HasUnmatchedClosingBracket(ReadOnlySpan<char> value, char closing)
    {
        var opening = closing switch
        {
            ')' => '(',
            ']' => '[',
            '}' => '{',
            _ => '\0'
        };

        var balance = 0;
        foreach (var current in value)
        {
            if (current == opening)
            {
                balance++;
            }
            else if (current == closing)
            {
                balance--;
            }
        }

        return balance < 0;
    }

    private sealed record ElementFrame(string Name, bool SuppressesText);
}

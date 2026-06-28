using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Domain.Mailings;

public static class HtmlMessageSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "br",
        "em",
        "h1",
        "h2",
        "h3",
        "li",
        "ol",
        "p",
        "span",
        "strong",
        "ul"
    };

    private static readonly HashSet<string> DiscardWithContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "iframe",
        "math",
        "meta",
        "object",
        "script",
        "style",
        "svg"
    };

    private static readonly Regex AttributeRegex = new(
        "([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*(?:=\\s*(\"([^\"]*)\"|'([^']*)'|([^\\s\"'<>`]+)))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HexColorRegex = new(
        "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RgbColorRegex = new(
        "^rgba?\\(\\s*(?:\\d{1,3}\\s*,\\s*){2}\\d{1,3}(?:\\s*,\\s*(?:0|1|0?\\.\\d+))?\\s*\\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex FontSizeRegex = new(
        "^(\\d{1,2})px$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var result = new StringBuilder(html.Length);
        var index = 0;
        while (index < html.Length)
        {
            var tagStart = html.IndexOf('<', index);
            if (tagStart < 0)
            {
                AppendText(result, html[index..]);
                break;
            }

            if (tagStart > index)
            {
                AppendText(result, html[index..tagStart]);
            }

            var tagEnd = html.IndexOf('>', tagStart + 1);
            if (tagEnd < 0)
            {
                AppendText(result, html[tagStart..]);
                break;
            }

            var rawTag = html[(tagStart + 1)..tagEnd].Trim();
            if (string.IsNullOrWhiteSpace(rawTag) ||
                rawTag.StartsWith("!", StringComparison.Ordinal) ||
                rawTag.StartsWith("?", StringComparison.Ordinal))
            {
                index = tagEnd + 1;
                continue;
            }

            var isClosing = rawTag.StartsWith("/", StringComparison.Ordinal);
            var tagContent = isClosing ? rawTag[1..].TrimStart() : rawTag;
            var tagName = ReadTagName(tagContent);
            if (string.IsNullOrWhiteSpace(tagName))
            {
                index = tagEnd + 1;
                continue;
            }

            var normalizedTag = NormalizeTagName(tagName);
            if (DiscardWithContentTags.Contains(normalizedTag))
            {
                index = isClosing ? tagEnd + 1 : SkipDiscardedContent(html, tagEnd + 1, normalizedTag);
                continue;
            }

            if (!AllowedTags.Contains(normalizedTag))
            {
                index = tagEnd + 1;
                continue;
            }

            if (isClosing)
            {
                if (normalizedTag != "br")
                {
                    result.Append("</").Append(normalizedTag).Append('>');
                }

                index = tagEnd + 1;
                continue;
            }

            var attributes = tagContent[tagName.Length..];
            AppendOpeningTag(result, normalizedTag, attributes);
            index = tagEnd + 1;
        }

        return result.ToString().Trim();
    }

    private static void AppendOpeningTag(StringBuilder result, string tagName, string attributes)
    {
        if (tagName == "br")
        {
            result.Append("<br>");
            return;
        }

        result.Append('<').Append(tagName);
        var safeAttributes = SafeAttributes(tagName, attributes);
        if (!string.IsNullOrWhiteSpace(safeAttributes))
        {
            result.Append(' ').Append(safeAttributes);
        }

        result.Append('>');
    }

    private static string SafeAttributes(string tagName, string attributes)
    {
        var href = string.Empty;
        var styles = new List<string>();

        foreach (Match match in AttributeRegex.Matches(attributes))
        {
            var name = match.Groups[1].Value.Trim().ToLowerInvariant();
            var value = HtmlDecode(match.Groups[3].Success
                ? match.Groups[3].Value
                : match.Groups[4].Success
                    ? match.Groups[4].Value
                    : match.Groups[5].Value);

            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tagName == "a" && name == "href")
            {
                href = SafeHref(value);
                continue;
            }

            if (name == "style")
            {
                styles.AddRange(SafeStyleDeclarations(value));
                continue;
            }

            if (tagName == "span" && name == "color")
            {
                var color = SafeColor(value);
                if (!string.IsNullOrWhiteSpace(color))
                {
                    styles.Add($"color:{color}");
                }
            }

            if (tagName == "span" && name == "size")
            {
                var fontSize = SafeLegacyFontSize(value);
                if (!string.IsNullOrWhiteSpace(fontSize))
                {
                    styles.Add($"font-size:{fontSize}");
                }
            }
        }

        if (tagName != "span")
        {
            styles.Clear();
        }

        var result = new List<string>();
        if (tagName == "a" && !string.IsNullOrWhiteSpace(href))
        {
            result.Add($"href=\"{H(href)}\"");
        }

        if (styles.Count > 0)
        {
            var style = string.Join(";", styles.Distinct(StringComparer.OrdinalIgnoreCase));
            result.Add($"style=\"{H(style)}\"");
        }

        return string.Join(" ", result);
    }

    private static IEnumerable<string> SafeStyleDeclarations(string value)
    {
        value = StripControlCharacters(value);
        if (ContainsUnsafeCss(value))
        {
            yield break;
        }

        foreach (var declaration in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var property = declaration[..separator].Trim().ToLowerInvariant();
            var propertyValue = declaration[(separator + 1)..].Trim();
            if (property == "color")
            {
                var color = SafeColor(propertyValue);
                if (!string.IsNullOrWhiteSpace(color))
                {
                    yield return $"color:{color}";
                }
            }
            else if (property == "font-size")
            {
                var fontSize = SafeFontSize(propertyValue);
                if (!string.IsNullOrWhiteSpace(fontSize))
                {
                    yield return $"font-size:{fontSize}";
                }
            }
        }
    }

    private static bool ContainsUnsafeCss(string value) =>
        value.Contains("url", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("expression", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("data:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains('<', StringComparison.Ordinal) ||
        value.Contains('>', StringComparison.Ordinal);

    private static string SafeHref(string value)
    {
        var href = StripControlCharacters(value).Trim();
        if (string.IsNullOrWhiteSpace(href) ||
            href.Contains('<', StringComparison.Ordinal) ||
            href.Contains('>', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto)
                ? uri.ToString()
                : string.Empty;
    }

    private static string SafeColor(string value)
    {
        var color = StripControlCharacters(value).Trim();
        if (HexColorRegex.IsMatch(color))
        {
            return color.ToLowerInvariant();
        }

        return RgbColorRegex.IsMatch(color) ? color : string.Empty;
    }

    private static string SafeFontSize(string value)
    {
        var fontSize = StripControlCharacters(value).Trim().ToLowerInvariant();
        var match = FontSizeRegex.Match(fontSize);
        if (!match.Success)
        {
            return string.Empty;
        }

        var pixels = int.Parse(match.Groups[1].Value);
        return pixels is >= 10 and <= 48 ? $"{pixels}px" : string.Empty;
    }

    private static string SafeLegacyFontSize(string value) => StripControlCharacters(value).Trim() switch
    {
        "1" => "10px",
        "2" => "13px",
        "3" => "16px",
        "4" => "18px",
        "5" => "22px",
        "6" => "28px",
        "7" => "36px",
        _ => string.Empty
    };

    private static int SkipDiscardedContent(string html, int startIndex, string tagName)
    {
        var closingTag = $"</{tagName}";
        var closingStart = html.IndexOf(closingTag, startIndex, StringComparison.OrdinalIgnoreCase);
        if (closingStart < 0)
        {
            return html.Length;
        }

        var closingEnd = html.IndexOf('>', closingStart + closingTag.Length);
        return closingEnd < 0 ? html.Length : closingEnd + 1;
    }

    private static string ReadTagName(string tagContent)
    {
        var length = 0;
        while (length < tagContent.Length && (char.IsLetterOrDigit(tagContent[length]) || tagContent[length] is '-' or ':'))
        {
            length++;
        }

        return length == 0 ? string.Empty : tagContent[..length];
    }

    private static string NormalizeTagName(string tagName) => tagName.ToLowerInvariant() switch
    {
        "b" => "strong",
        "font" => "span",
        "i" => "em",
        _ => tagName.ToLowerInvariant()
    };

    private static void AppendText(StringBuilder result, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        result.Append(H(HtmlDecode(value)));
    }

    private static string StripControlCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string HtmlDecode(string value) => WebUtility.HtmlDecode(value ?? string.Empty);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

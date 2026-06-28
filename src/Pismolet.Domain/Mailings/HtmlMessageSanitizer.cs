using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Domain.Mailings;

public static class HtmlMessageSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "article",
        "body",
        "blockquote",
        "br",
        "center",
        "col",
        "colgroup",
        "code",
        "del",
        "div",
        "em",
        "footer",
        "h1",
        "h2",
        "h3",
        "head",
        "header",
        "hr",
        "html",
        "img",
        "ins",
        "li",
        "main",
        "meta",
        "nav",
        "ol",
        "p",
        "pre",
        "section",
        "small",
        "s",
        "sub",
        "sup",
        "span",
        "strong",
        "table",
        "tbody",
        "td",
        "tfoot",
        "th",
        "thead",
        "title",
        "tr",
        "u",
        "ul"
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br",
        "hr",
        "img"
    };

    private static readonly HashSet<string> DiscardWithContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "iframe",
        "base",
        "button",
        "embed",
        "form",
        "input",
        "link",
        "math",
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
                if (!VoidTags.Contains(normalizedTag))
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

    public static HtmlMessageValidationResult Validate(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return HtmlMessageValidationResult.Success();
        }

        var index = 0;
        while (index < html.Length)
        {
            var tagStart = html.IndexOf('<', index);
            if (tagStart < 0)
            {
                return HtmlMessageValidationResult.Success();
            }

            var tagEnd = html.IndexOf('>', tagStart + 1);
            if (tagEnd < 0)
            {
                return HtmlMessageValidationResult.Failure("HTML содержит незакрытый тег. Проверьте код письма.");
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
                return HtmlMessageValidationResult.Failure("HTML содержит некорректный тег. Проверьте код письма.");
            }

            var normalizedTag = NormalizeTagName(tagName);
            if (DiscardWithContentTags.Contains(normalizedTag))
            {
                return HtmlMessageValidationResult.Failure($"HTML содержит запрещённый тег <{normalizedTag}>. Удалите его и попробуйте снова.");
            }

            if (!AllowedTags.Contains(normalizedTag))
            {
                return HtmlMessageValidationResult.Failure($"HTML содержит неподдерживаемый тег <{normalizedTag}>. Удалите его или замените простым HTML.");
            }

            if (!isClosing)
            {
                var attributes = tagContent[tagName.Length..];
                var attributeValidation = ValidateAttributes(normalizedTag, attributes);
                if (!attributeValidation.Ok)
                {
                    return attributeValidation;
                }
            }

            index = tagEnd + 1;
        }

        return HtmlMessageValidationResult.Success();
    }

    private static void AppendOpeningTag(StringBuilder result, string tagName, string attributes)
    {
        if (tagName == "br" || tagName == "hr")
        {
            result.Append('<').Append(tagName).Append('>');
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
        var src = string.Empty;
        var alt = string.Empty;
        var width = string.Empty;
        var height = string.Empty;

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

            if (tagName == "img" && name == "src")
            {
                src = SafeImageSrc(value);
                continue;
            }

            if (tagName == "img" && name == "alt")
            {
                alt = SafePlainAttribute(value, maxLength: 180);
                continue;
            }

            if (tagName == "img" && name is "width" or "height")
            {
                var dimension = SafeDimension(value);
                if (string.IsNullOrWhiteSpace(dimension))
                {
                    continue;
                }

                if (name == "width")
                {
                    width = dimension;
                }
                else
                {
                    height = dimension;
                }

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

        if (tagName == "img" && !string.IsNullOrWhiteSpace(src))
        {
            result.Add($"src=\"{H(src)}\"");
            if (!string.IsNullOrWhiteSpace(alt))
            {
                result.Add($"alt=\"{H(alt)}\"");
            }

            if (!string.IsNullOrWhiteSpace(width))
            {
                result.Add($"width=\"{H(width)}\"");
            }

            if (!string.IsNullOrWhiteSpace(height))
            {
                result.Add($"height=\"{H(height)}\"");
            }
        }

        if (styles.Count > 0)
        {
            var style = string.Join(";", styles.Distinct(StringComparer.OrdinalIgnoreCase));
            result.Add($"style=\"{H(style)}\"");
        }

        return string.Join(" ", result);
    }

    private static HtmlMessageValidationResult ValidateAttributes(string tagName, string attributes)
    {
        foreach (Match match in AttributeRegex.Matches(attributes))
        {
            var name = match.Groups[1].Value.Trim();
            var normalizedName = name.ToLowerInvariant();
            var value = HtmlDecode(match.Groups[3].Success
                ? match.Groups[3].Value
                : match.Groups[4].Success
                    ? match.Groups[4].Value
                    : match.Groups[5].Value);

            if (normalizedName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                return HtmlMessageValidationResult.Failure($"HTML содержит запрещённый обработчик события {normalizedName}. Удалите атрибуты onclick/onload и попробуйте снова.");
            }

            if (normalizedName == "style" && ContainsUnsafeCss(value))
            {
                return HtmlMessageValidationResult.Failure("HTML содержит небезопасный CSS в атрибуте style. Уберите url(), expression, javascript: и data:.");
            }

            if (tagName == "a" && normalizedName == "href" && !IsSafeHref(value))
            {
                return HtmlMessageValidationResult.Failure("HTML содержит запрещённую ссылку. В ссылках разрешены только http, https и mailto.");
            }

            if (tagName == "img" && normalizedName == "src" && !IsSafeImageSrc(value))
            {
                return HtmlMessageValidationResult.Failure("HTML содержит запрещённый адрес картинки. Для изображений разрешены только http и https.");
            }

            if (ContainsUnsafeAttributeValue(value))
            {
                return HtmlMessageValidationResult.Failure($"HTML содержит запрещённое значение атрибута {normalizedName}. Уберите javascript:, data: и другой исполняемый код.");
            }
        }

        return HtmlMessageValidationResult.Success();
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
        value.Contains("vbscript", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("data:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains('<', StringComparison.Ordinal) ||
        value.Contains('>', StringComparison.Ordinal);

    private static bool ContainsUnsafeAttributeValue(string value)
    {
        var normalized = StripControlCharacters(value).Trim();
        return normalized.Contains('<', StringComparison.Ordinal) ||
            normalized.Contains('>', StringComparison.Ordinal) ||
            normalized.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("vbscript:", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("data:", StringComparison.OrdinalIgnoreCase);
    }

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

    private static bool IsSafeHref(string value)
    {
        var href = StripControlCharacters(value).Trim();
        return Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto);
    }

    private static string SafeImageSrc(string value)
    {
        var src = StripControlCharacters(value).Trim();
        if (string.IsNullOrWhiteSpace(src) ||
            src.Contains('<', StringComparison.Ordinal) ||
            src.Contains('>', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return Uri.TryCreate(src, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.ToString()
                : string.Empty;
    }

    private static bool IsSafeImageSrc(string value)
    {
        var src = StripControlCharacters(value).Trim();
        return Uri.TryCreate(src, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string SafePlainAttribute(string value, int maxLength)
    {
        var text = StripControlCharacters(value).Trim();
        if (text.Length > maxLength)
        {
            text = text[..maxLength];
        }

        return text;
    }

    private static string SafeDimension(string value)
    {
        var dimension = StripControlCharacters(value).Trim();
        if (Regex.IsMatch(dimension, "^\\d{1,4}$", RegexOptions.CultureInvariant))
        {
            return dimension;
        }

        return Regex.IsMatch(dimension, "^\\d{1,4}px$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            ? dimension.ToLowerInvariant()
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

public sealed record HtmlMessageValidationResult(bool Ok, string Error)
{
    public static HtmlMessageValidationResult Success() => new(true, string.Empty);

    public static HtmlMessageValidationResult Failure(string error) => new(false, error);
}

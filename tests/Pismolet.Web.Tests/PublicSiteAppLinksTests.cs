namespace Pismolet.Web.Tests;

public sealed class PublicSiteAppLinksTests
{
    private const string AppLoginUrl = "https://app.pismolet.ru/account/login";
    private const string AppRegisterUrl = "https://app.pismolet.ru/account/register";

    [Fact]
    public void Public_pages_with_site_header_link_to_app_login_and_registration()
    {
        foreach (var page in PublicHtmlPagesWithHeader())
        {
            var html = File.ReadAllText(page);

            Assert.Contains($"href=\"{AppLoginUrl}\"", html);
            Assert.Contains($"href=\"{AppRegisterUrl}\"", html);
            Assert.DoesNotContain("href=\"/account/login\"", html);
            Assert.DoesNotContain("href=\"/account/register\"", html);
        }
    }

    [Fact]
    public void Public_launch_pages_use_app_registration_as_primary_start_path()
    {
        var pages = new[]
        {
            "public_html/index.htm",
            "public_html/pricing/index.html",
            "public_html/how-it-works/index.html",
            "public_html/bcc-alternative/index.html",
            "public_html/excel-email-list/index.html",
            "public_html/no-domain-setup/index.html",
            "public_html/for-small-business/index.html",
            "public_html/for-nko/index.html",
            "public_html/for-events/index.html",
            "public_html/articles/bez-skrytyh-kopiy/index.html",
            "public_html/articles/rassylka-iz-excel/index.html",
            "public_html/articles/bez-nastroiki-domena/index.html",
            "public_html/articles/email-rassylka-dlya-malogo-biznesa/index.html"
        };

        foreach (var relativePath in pages)
        {
            var html = ReadRepoFile(relativePath);

            Assert.Contains($"class=\"btn\" href=\"{AppRegisterUrl}\"", html);
            Assert.DoesNotContain("class=\"btn\" href=\"/contacts/\"", html);
        }
    }

    [Fact]
    public void Public_primary_launch_pages_show_existing_account_hint()
    {
        var pages = new[]
        {
            "public_html/index.htm",
            "public_html/pricing/index.html",
            "public_html/how-it-works/index.html",
            "public_html/bcc-alternative/index.html",
            "public_html/excel-email-list/index.html",
            "public_html/no-domain-setup/index.html",
            "public_html/for-small-business/index.html",
            "public_html/for-nko/index.html",
            "public_html/for-events/index.html"
        };

        foreach (var relativePath in pages)
        {
            var html = ReadRepoFile(relativePath);

            Assert.Contains("Уже есть аккаунт?", html);
            Assert.Contains($"href=\"{AppLoginUrl}\"", html);
        }
    }

    [Fact]
    public void Public_site_does_not_expose_prelaunch_request_copy()
    {
        var forbidden = new[]
        {
            "Оставить заявку",
            "Оставьте заявку",
            "Уточнить запуск",
            "первые подключения",
            "первым тестовым отправкам"
        };

        foreach (var page in PublicHtmlPages())
        {
            var html = File.ReadAllText(page);

            foreach (var text in forbidden)
            {
                Assert.DoesNotContain(text, html);
            }
        }
    }

    [Fact]
    public void Public_sitemap_and_canonicals_remain_public_only()
    {
        var sitemap = ReadRepoFile("public_html/sitemap.xml");

        Assert.DoesNotContain("app.pismolet.ru", sitemap);

        foreach (var page in PublicHtmlPages())
        {
            var html = File.ReadAllText(page);
            if (!html.Contains("rel=\"canonical\"", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain("rel=\"canonical\" href=\"https://app.pismolet.ru", html);
            Assert.DoesNotContain("rel=\"canonical\" href=\"/account/", html);
        }
    }

    [Fact]
    public void Public_css_defines_responsive_app_actions()
    {
        var css = ReadRepoFile("public_html/assets/site.css");

        Assert.Contains(".app-actions", css);
        Assert.Contains(".app-actions .btn", css);
        Assert.Contains(".account-hint a", css);
        Assert.Contains(".footer-links", css);
        Assert.Contains("@media (max-width: 520px)", css);
        Assert.Contains(@"content: ""\2713"";", css);
        Assert.DoesNotContain(@"content: ""\\2713"";", css);
    }

    private static IReadOnlyCollection<string> PublicHtmlPagesWithHeader()
    {
        return PublicHtmlPages()
            .Where(path => File.ReadAllText(path).Contains("site-header", StringComparison.Ordinal))
            .ToArray();
    }

    private static IReadOnlyCollection<string> PublicHtmlPages()
    {
        var root = FindRepoRoot();
        var publicRoot = Path.Combine(root, "public_html");

        return Directory
            .EnumerateFiles(publicRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string ReadRepoFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pismolet.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}

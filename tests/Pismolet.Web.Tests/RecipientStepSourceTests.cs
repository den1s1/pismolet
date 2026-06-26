namespace Pismolet.Web.Tests;

public sealed class RecipientStepSourceTests
{
    [Fact]
    public void Program_does_not_use_regex_recipient_declaration_middleware()
    {
        var program = ReadRepositoryFile("src/Pismolet.Web/Program.cs");

        Assert.DoesNotContain("UseCompactRecipientDeclarationUi", program);
        Assert.False(File.Exists(RepositoryPath("src/Pismolet.Web/Endpoints/CompactRecipientDeclarationUiMiddlewareExtensions.cs")));
    }

    [Fact]
    public void Recipient_step_renders_final_address_blocks_directly()
    {
        var endpoint = ReadRepositoryFile("src/Pismolet.Web/Endpoints/SimplifiedRecipientStepEndpoints.cs");

        Assert.Contains("MapPost(\"/mailings/{id:guid}/recipients\"", endpoint);
        Assert.Contains("address-summary-block", endpoint);
        Assert.Contains("address-base-block", endpoint);
        Assert.Contains("address-list-block", endpoint);
        Assert.Contains("address-inline-form address-search-form", endpoint);
        Assert.Contains("address-inline-form address-add-form", endpoint);
        Assert.DoesNotContain("style='", endpoint);
    }

    private static string ReadRepositoryFile(string relativePath) => File.ReadAllText(RepositoryPath(relativePath));

    private static string RepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pismolet.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }
}

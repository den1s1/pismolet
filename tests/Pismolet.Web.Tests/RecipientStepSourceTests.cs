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
    public void Program_does_not_register_legacy_wizard_endpoint_layers()
    {
        var program = ReadRepositoryFile("src/Pismolet.Web/Program.cs");

        Assert.DoesNotContain("MapMailingCreationFlowReworkEndpoints", program);
        Assert.DoesNotContain("MapSimplifiedRecipientStepEndpoints", program);
        Assert.DoesNotContain("MapSimplifiedMessageStepEndpoints", program);
        Assert.False(File.Exists(RepositoryPath("src/Pismolet.Web/Endpoints/MailingCreationFlowReworkEndpoints.cs")));
        Assert.False(File.Exists(RepositoryPath("src/Pismolet.Web/Endpoints/SimplifiedRecipientStepEndpoints.cs")));
        Assert.False(File.Exists(RepositoryPath("src/Pismolet.Web/Endpoints/SimplifiedMessageStepEndpoints.cs")));
    }

    [Fact]
    public void Program_registers_confirmation_submit_endpoint()
    {
        var program = ReadRepositoryFile("src/Pismolet.Web/Program.cs");

        Assert.Contains("MapMailingConfirmationSubmitEndpoints", program);
        Assert.True(File.Exists(RepositoryPath("src/Pismolet.Web/Endpoints/MailingConfirmationSubmitEndpoints.cs")));
    }

    [Fact]
    public void Recipient_flow_renders_final_address_blocks_directly()
    {
        var reviewEndpoint = ReadRepositoryFile("src/Pismolet.Web/Endpoints/MailingRecipientReviewOverlayEndpoints.cs");
        var managementEndpoint = ReadRepositoryFile("src/Pismolet.Web/Endpoints/MailingRecipientManagementEndpoints.cs");

        Assert.Contains("MapPost(\"/mailings/{id:guid}/recipients\"", reviewEndpoint);
        Assert.Contains("address-summary-block", reviewEndpoint);
        Assert.Contains("address-list-block", reviewEndpoint);
        Assert.Contains("address-inline-form address-search-form", reviewEndpoint);
        Assert.Contains("address-inline-form address-add-form", reviewEndpoint);
        Assert.Contains("MapPost(\"/mailings/{id:guid}/recipients/add\"", managementEndpoint);
        Assert.Contains("MapPost(\"/mailings/{id:guid}/recipients/remove\"", managementEndpoint);
        Assert.DoesNotContain("style='", reviewEndpoint);
        Assert.DoesNotContain("style='", managementEndpoint);
    }

    [Fact]
    public void Dashboard_endpoints_do_not_register_legacy_wizard_routes()
    {
        var endpoint = ReadRepositoryFile("src/Pismolet.Web/Endpoints/DashboardEndpoints.cs");

        Assert.DoesNotContain("MapGet(\"/mailings/new\"", endpoint);
        Assert.DoesNotContain("MapPost(\"/mailings\"", endpoint);
        Assert.DoesNotContain("MapGet(\"/mailings/{id:guid}/recipients\"", endpoint);
        Assert.DoesNotContain("MapPost(\"/mailings/{id:guid}/recipients\"", endpoint);
        Assert.DoesNotContain("MapGet(\"/mailings/{id:guid}/declaration\"", endpoint);
        Assert.DoesNotContain("MapPost(\"/mailings/{id:guid}/declaration\"", endpoint);
        Assert.DoesNotContain("ShowUploadForm", endpoint);
        Assert.DoesNotContain("ImportResultWizard", endpoint);
        Assert.DoesNotContain("ShowDeclaration", endpoint);
        Assert.DoesNotContain("ConfirmDeclaration", endpoint);
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

using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Mail;
using Pismolet.Web.Infrastructure.Persistence;

namespace Pismolet.Web.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPismoletWebServices(this IServiceCollection services)
    {
        // Временные регистрации Sprint 0-3 для локальной разработки.
        services.AddSingleton<IUserRepository, InMemoryUserRepository>();
        services.AddSingleton<IMailingRepository, InMemoryMailingRepository>();
        services.AddSingleton<IGlobalSuppressionRepository, InMemoryGlobalSuppressionRepository>();
        services.AddSingleton<IFakeMailer, InMemoryFakeMailer>();
        services.AddSingleton<IAuditLogger, InMemoryAuditLogger>();

        services.AddSingleton<IEmailNormalizer, EmailNormalizer>();
        services.AddSingleton<IEmailSyntaxValidator, EmailSyntaxValidator>();
        services.AddSingleton<IUserAccountService, UserAccountService>();
        services.AddSingleton<IMailingService, MailingService>();
        services.AddSingleton<IRecipientImportService, RecipientImportService>();
        services.AddSingleton<IMailingDeclarationService, MailingDeclarationService>();
        services.AddSingleton<IMailingMessageService, MailingMessageService>();
        services.AddSingleton<IUnsubscribeTokenService, DevUnsubscribeTokenService>();
        services.AddSingleton<IMessageRenderingService, MessageRenderingService>();

        return services;
    }
}

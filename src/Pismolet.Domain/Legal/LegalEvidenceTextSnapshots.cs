namespace Pismolet.Web.Domain.Legal;

public static class LegalEvidenceTextSnapshots
{
    public const string BaseSourceSelectionText =
        "Клиент выбрал источник загруженной адресной базы и подтвердил, что использует его как основание для дальнейшей проверки законности базы.";

    public const string RecipientDataProcessingInstructionText =
        "Поручаю Письмолёту технически обработать загруженные email-адреса: проверить формат, исключить дубли, ранее отписавшиеся и заблокированные адреса, рассчитать стоимость, отправить письма, обработать отписки, жалобы и ошибки доставки.";

    public const string AdvertisingConsentText =
        "Для рекламной рассылки подтверждаю, что у меня есть предварительное согласие адресатов на получение рекламных сообщений.";

    public const string CampaignLaunchConfirmationText =
        "Клиент подтвердил финальный запуск рассылки и поручил Письмолёту поставить письма в очередь отправки.";
}

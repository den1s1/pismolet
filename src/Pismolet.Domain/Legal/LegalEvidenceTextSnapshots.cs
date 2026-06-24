namespace Pismolet.Web.Domain.Legal;

public static class LegalEvidenceTextSnapshots
{
    public const string CurrentVersion = "2026-06-16-v1";

    public const string OfferAndRulesAcceptanceText =
        "Клиент принимает правила сервиса Письмолёт и условия использования сервиса.";

    public const string ClientPersonalDataConsentText =
        "Клиент даёт согласие на обработку данных, указанных при регистрации: ФИО, email и телефон.";

    public const string ClientProfileConfirmationText =
        "Клиент подтвердил актуальность данных профиля, email для входа, отправителя по умолчанию и email для пересылки ответов.";

    public const string BaseSourceSelectionText =
        "Клиент выбрал источник загруженной адресной базы и подтвердил, что использует его как основание для дальнейшей проверки законности базы.";

    public const string RecipientDataProcessingInstructionText =
        "Поручаю Письмолёту технически обработать загруженные email-адреса: проверить формат, исключить дубли, ранее отписавшиеся и заблокированные адреса, рассчитать стоимость, отправить письма, обработать отписки, жалобы и ошибки доставки.";

    public const string AdvertisingConsentText =
        "Для рекламной рассылки подтверждаю, что у меня есть предварительное согласие адресатов на получение рекламных сообщений.";

    public const string CampaignLaunchConfirmationText =
        "Клиент подтвердил финальный запуск рассылки и поручил Письмолёту поставить письма в очередь отправки.";

    public const string GlobalUnsubscribeConfirmationText =
        "Получатель подтвердил отписку от писем через сервис Письмолёт по ссылке отписки.";

    public const string RecipientComplaintReceivedText =
        "Письмолёт получил от почтового провайдера событие жалобы получателя на письмо или его пометку как нежелательного.";
}

namespace Pismolet.Web.Domain.Legal;

public sealed record LegalDocumentVersion(
    Guid Id,
    string DocumentKey,
    string Version,
    string TextHash,
    string Text,
    string? Url,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record LegalEvidenceEvent(
    Guid Id,
    string EventType,
    string ClientId,
    string? UserId,
    Guid? ImportBatchId,
    Guid? MailingId,
    string? DocumentKey,
    string? DocumentVersion,
    string? TextHash,
    string? EventTextSnapshot,
    string Result,
    string? Ip,
    string? UserAgent,
    string? Route,
    string MetadataJson,
    DateTimeOffset CreatedAt);

public static class LegalDocumentKeys
{
    public const string OfferAndRules = "offer_and_rules";
    public const string MailingRules = "mailing_rules";
    public const string PrivacyPolicy = "privacy_policy";
    public const string ClientPersonalDataConsent = "client_personal_data_consent";
    public const string ClientProfileConfirmation = "client_profile_confirmation";
    public const string BaseSourceSelection = "base_source_selection";
    public const string BaseLawfulnessDeclaration = "base_lawfulness_declaration";
    public const string RecipientDataProcessingInstruction = "recipient_data_processing_instruction";
    public const string AdvertisingConsentDeclaration = "advertising_consent_declaration";
    public const string AntiSpamPolicy = "anti_spam_policy";
    public const string ProhibitedContentPolicy = "prohibited_content_policy";
    public const string PaymentAndRefundRules = "payment_and_refund_rules";
    public const string ReplyRetentionPolicy = "reply_retention_policy";
    public const string ServiceEmailFooter = "service_email_footer";
    public const string CampaignLaunchConfirmation = "campaign_launch_confirmation";
    public const string GlobalUnsubscribeConfirmation = "global_unsubscribe_confirmation";
    public const string RecipientComplaint = "recipient_complaint";
}

public static class LegalEventTypes
{
    public const string OfferAndRulesAccepted = "offer_and_rules_accepted";
    public const string ClientPersonalDataConsentAccepted = "client_personal_data_consent_accepted";
    public const string ClientProfileConfirmed = "client_profile_confirmed";
    public const string ClientEmailConfirmed = "client_email_confirmed";
    public const string BaseSourceSelected = "base_source_selected";
    public const string BaseLawfulnessDeclared = "base_lawfulness_declared";
    public const string RecipientDataProcessingInstructionAccepted = "recipient_data_processing_instruction_accepted";
    public const string AdvertisingConsentDeclared = "advertising_consent_declared";
    public const string CampaignLaunchConfirmedBeforePayment = "campaign_launch_confirmed_before_payment";
    public const string ProofRequestedFromClient = "proof_requested_from_client";
    public const string GlobalUnsubscribeConfirmed = "global_unsubscribe_confirmed";
    public const string RecipientComplaintReceived = "recipient_complaint_received";
}

public static class LegalEventResults
{
    public const string Accepted = "accepted";
    public const string Declared = "declared";
    public const string Confirmed = "confirmed";
    public const string Requested = "requested";
    public const string Received = "received";
}

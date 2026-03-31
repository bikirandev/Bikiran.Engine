using Bikiran.Engine.Core;
using Bikiran.Engine.Credentials;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Sends an email using a registered SMTP credential (via MailKit).
/// </summary>
public class EmailSendNode : IFlowNode
{
    public string Name { get; }
    public FlowNodeType NodeType => FlowNodeType.EmailSend;

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <summary>Recipient email address (required).</summary>
    public string ToEmail { get; set; } = "";

    /// <summary>Recipient display name.</summary>
    public string ToName { get; set; } = "";

    /// <summary>Email subject (required).</summary>
    public string Subject { get; set; } = "";

    /// <summary>Named SMTP credential registered at startup.</summary>
    public string? CredentialName { get; set; }

    /// <summary>Template key. When set, the email body is generated from this template key.</summary>
    public string? Template { get; set; }

    /// <summary>Raw HTML body.</summary>
    public string? HtmlBody { get; set; }

    /// <summary>Plain text body.</summary>
    public string? TextBody { get; set; }

    /// <summary>Dynamically builds the HTML body from context.</summary>
    public Func<FlowContext, string>? HtmlBodyResolver { get; set; }

    /// <summary>Dynamically builds the plain text body from context.</summary>
    public Func<FlowContext, string>? TextBodyResolver { get; set; }

    /// <summary>Template placeholder values.</summary>
    public Dictionary<string, string> Placeholders { get; set; } = new();

    /// <summary>Dynamically builds placeholder values from context.</summary>
    public Func<FlowContext, Dictionary<string, string>>? PlaceholderResolver { get; set; }

    public EmailSendNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ToEmail))
            return NodeResult.Fail("EmailSendNode: ToEmail is required.");
        if (string.IsNullOrWhiteSpace(Subject))
            return NodeResult.Fail("EmailSendNode: Subject is required.");

        // Resolve credential
        SmtpCredential? cred = null;
        if (!string.IsNullOrWhiteSpace(CredentialName))
            cred = context.GetCredential<SmtpCredential>(CredentialName);

        if (cred == null)
            return NodeResult.Fail("EmailSendNode: A SmtpCredential must be registered and referenced via CredentialName.");

        // Resolve body
        string? htmlBody = null;
        string? textBody = null;

        if (!string.IsNullOrWhiteSpace(Template))
        {
            var placeholders = PlaceholderResolver != null
                ? PlaceholderResolver(context)
                : Placeholders;

            htmlBody = $"<p>Template: {Template}</p>" +
                       string.Join("", placeholders.Select(p => $"<p>{p.Key}: {p.Value}</p>"));
        }
        else if (HtmlBodyResolver != null)
        {
            htmlBody = HtmlBodyResolver(context);
        }
        else if (!string.IsNullOrWhiteSpace(HtmlBody))
        {
            htmlBody = HtmlBody;
        }
        else if (TextBodyResolver != null)
        {
            textBody = TextBodyResolver(context);
        }
        else if (!string.IsNullOrWhiteSpace(TextBody))
        {
            textBody = TextBody;
        }
        else
        {
            return NodeResult.Fail("EmailSendNode: At least one body source (Template, HtmlBody, TextBody, or their Resolver variants) must be provided.");
        }

        try
        {
            var message = new MimeMessage();
            var fromEmail = string.IsNullOrEmpty(cred.FromEmail) ? cred.Username : cred.FromEmail;
            message.From.Add(new MailboxAddress(cred.FromName, fromEmail));
            message.To.Add(new MailboxAddress(ToName, ToEmail));
            message.Subject = Subject;

            var bodyBuilder = new BodyBuilder();
            if (htmlBody != null) bodyBuilder.HtmlBody = htmlBody;
            if (textBody != null) bodyBuilder.TextBody = textBody;
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(cred.Host, cred.Port,
                cred.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                cancellationToken);
            await client.AuthenticateAsync(cred.Username, cred.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return NodeResult.Ok($"Email sent to {ToEmail}");
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Failed to send email: {ex.Message}");
        }
    }
}

namespace Bikiran.Engine.Credentials;

/// <summary>
/// SMTP email credential registered at startup and referenced by name in EmailSendNode.
/// </summary>
public class SmtpCredential : IEngineCredential
{
    /// <summary>SMTP server hostname (e.g., "smtp.example.com").</summary>
    public string Host { get; set; } = "";

    /// <summary>SMTP port (e.g., 587 for TLS, 465 for SSL).</summary>
    public int Port { get; set; } = 587;

    /// <summary>SMTP authentication username.</summary>
    public string Username { get; set; } = "";

    /// <summary>SMTP authentication password.</summary>
    public string Password { get; set; } = "";

    /// <summary>Whether to use SSL/TLS. Default is true.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>Override the From email address. Defaults to Username if empty.</summary>
    public string FromEmail { get; set; } = "";

    /// <summary>Display name shown in the From field.</summary>
    public string FromName { get; set; } = "";
}

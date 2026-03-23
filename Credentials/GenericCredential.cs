namespace Bikiran.Engine.Credentials;

/// <summary>
/// A flexible key-value credential for external services, APIs, and anything that doesn't fit SmtpCredential.
/// </summary>
public class GenericCredential : IEngineCredential
{
    /// <summary>Key-value pairs for storing API keys, base URLs, secrets, etc.</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}

namespace ApiHealthDashboard.Configuration;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "ApiHealthDashboard";
}

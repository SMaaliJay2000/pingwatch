using System.Net.Mail;
using PingWatch.Application.Email;

namespace PingWatch.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 25;
    public string From { get; init; } = "";
}

// Development only (Mailpit): no TLS or login. Production gets a Resend or Postmark sender.
internal sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(options.Host, options.Port);
        using var mail = new MailMessage(options.From, message.To, message.Subject, message.TextBody);

        await client.SendMailAsync(mail, cancellationToken);
    }
}

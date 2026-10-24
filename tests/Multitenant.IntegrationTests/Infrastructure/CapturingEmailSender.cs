using System.Collections.Concurrent;
using Multitenant.Application.Abstractions.Messaging;

namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces the real email sender in tests so confirmation links can be inspected
/// without a mail server.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Messages { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        while (Messages.TryDequeue(out _))
        {
        }
    }
}

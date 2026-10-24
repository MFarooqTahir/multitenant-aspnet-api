namespace Multitenant.Application.Abstractions;

/// <summary>A provider-neutral email message.</summary>
public sealed record EmailMessage(string ToAddress, string Subject, string HtmlBody, string TextBody);

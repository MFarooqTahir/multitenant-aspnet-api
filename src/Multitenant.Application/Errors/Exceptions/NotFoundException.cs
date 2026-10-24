namespace Multitenant.Application.Errors;

/// <summary>
/// The resource does not exist <em>in the current tenant</em>. Cross-tenant ids
/// intentionally resolve to this error rather than 403 to avoid confirming existence.
/// </summary>
public sealed class NotFoundException(string message = "The requested resource was not found.", string code = "not_found")
    : AppException(message, 404, code);

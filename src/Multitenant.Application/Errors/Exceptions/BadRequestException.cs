namespace Multitenant.Application.Errors.Exceptions;

/// <summary>A malformed or invalid client request that cannot be processed.</summary>
public sealed class BadRequestException(string message, string code = "bad_request")
    : AppException(message, 400, code);

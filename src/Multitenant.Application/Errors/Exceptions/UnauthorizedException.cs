namespace Multitenant.Application.Errors.Exceptions;

public sealed class UnauthorizedException(string message = "Invalid credentials.", string code = "unauthorized")
    : AppException(message, 401, code);

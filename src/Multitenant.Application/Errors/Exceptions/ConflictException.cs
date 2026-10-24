namespace Multitenant.Application.Errors.Exceptions;

public sealed class ConflictException(string message, string code = "conflict")
    : AppException(message, 409, code);

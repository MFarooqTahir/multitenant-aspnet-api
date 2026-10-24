namespace Multitenant.Application.Errors;

public sealed class ConflictException(string message, string code = "conflict")
    : AppException(message, 409, code);

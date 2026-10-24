namespace Multitenant.Application.Errors;

public sealed class ForbiddenException(string message = "You are not allowed to perform this action.", string code = "forbidden")
    : AppException(message, 403, code);

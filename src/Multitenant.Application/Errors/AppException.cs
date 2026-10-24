namespace Multitenant.Application.Errors;

public abstract class AppException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;
}

public sealed class NotFoundException(string message = "The requested resource was not found.", string code = "not_found")
    : AppException(message, 404, code);

public sealed class ConflictException(string message, string code = "conflict")
    : AppException(message, 409, code);

public sealed class UnauthorizedException(string message = "Invalid credentials.", string code = "unauthorized")
    : AppException(message, 401, code);

public sealed class ForbiddenException(string message = "You are not allowed to perform this action.", string code = "forbidden")
    : AppException(message, 403, code);

public sealed class FeatureDisabledException(string message = "This feature is disabled.", string code = "feature_disabled")
    : AppException(message, 404, code);

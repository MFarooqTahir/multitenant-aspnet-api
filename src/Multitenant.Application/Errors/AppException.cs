namespace Multitenant.Application.Errors;

/// <summary>
/// Base class for expected failures that map to an HTTP status and a stable
/// machine-readable <c>code</c>. The API turns these into RFC 7807 responses.
/// </summary>
public abstract class AppException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;
}

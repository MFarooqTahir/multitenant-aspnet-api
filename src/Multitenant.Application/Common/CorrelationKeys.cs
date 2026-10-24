namespace Multitenant.Application.Common;

/// <summary>
/// Keys for values shared through <c>HttpContext.Items</c>.
/// </summary>
public static class CorrelationKeys
{
    public const string HeaderName = "X-Correlation-Id";

    public const string ItemKey = "correlation-id";
}

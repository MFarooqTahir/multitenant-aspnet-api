namespace Multitenant.Application.Errors;

/// <summary>
/// A feature is compiled in but disabled by configuration. Exposed as 404 so the
/// API does not advertise capabilities that are switched off.
/// </summary>
public sealed class FeatureDisabledException(string message = "This feature is disabled.", string code = "feature_disabled")
    : AppException(message, 404, code);

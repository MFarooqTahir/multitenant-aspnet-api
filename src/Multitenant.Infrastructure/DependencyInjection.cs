using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Multitenant.Application.Abstractions.Identity;
using Multitenant.Application.Abstractions.Messaging;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common.Security;
using Multitenant.Application.Configuration.Auth;
using Multitenant.Application.Configuration.Database;
using Multitenant.Application.Configuration.Email;
using Multitenant.Application.Configuration.Identity;
using Multitenant.Application.Configuration.Jwt;
using Multitenant.Application.Configuration.RateLimiting;
using Multitenant.Application.Configuration.Web;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Email;
using Multitenant.Infrastructure.Identity;
using Multitenant.Infrastructure.Persistence;
using Multitenant.Infrastructure.Services;
using Multitenant.Infrastructure.Tenancy;

namespace Multitenant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        AddOptions(services, configuration);

        services.AddScoped<TenantAuditingInterceptor>();
        services.AddScoped<TenantRowLock>();
        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            // Resolved lazily so configuration sources added later (tests, secrets,
            // environment-specific files) are honoured.
            var resolvedConnectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Postgres");

            if (string.IsNullOrWhiteSpace(resolvedConnectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Postgres is not configured. Set it in appsettings.json, user secrets or the ConnectionStrings__Postgres environment variable.");
            }

            options.UseNpgsql(resolvedConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
            });

            options.AddInterceptors(provider.GetRequiredService<TenantAuditingInterceptor>());
        });

        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = false;

                // The user name is an internal "{slug}:{email}" key, not a login
                // identifier: uniqueness comes from the store. Identity's default
                // character whitelist would reject valid addresses containing
                // apostrophes, ampersands or non-ASCII characters, so it is disabled.
                options.User.AllowedUserNameCharacters = string.Empty;

                // One source of truth for the password policy: the same constants
                // feed FluentValidation and Identity.
                options.Password.RequiredLength = PasswordRules.MinimumLength;
                options.Password.RequireDigit = PasswordRules.RequireDigit;
                options.Password.RequireLowercase = PasswordRules.RequireLowercase;
                options.Password.RequireUppercase = PasswordRules.RequireUppercase;
                options.Password.RequireNonAlphanumeric = PasswordRules.RequireNonAlphanumeric;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Identity's own sign-in gates stay off on purpose. SignInManager checks
        // them before the password, which would let an anonymous caller tell an
        // unconfirmed address (403 email_not_confirmed) from an unknown one (401).
        // AuthService enforces Identity:RequireConfirmedEmail explicitly, after the
        // password has been verified.
        services.AddOptions<IdentityOptions>()
            .Configure(identity =>
            {
                identity.SignIn.RequireConfirmedEmail = false;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwt) => JwtBearerConfiguration.Configure(options, jwt.Value));

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<RefreshTokenManager>();
        services.AddScoped<RefreshTokenCleaner>();
        services.AddHostedService<RefreshTokenCleanupService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<ITenantFeatureService, TenantFeatureService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IAccountEmailService, AccountEmailService>();
        services.AddScoped<DatabaseInitializer>();

        services.AddScoped<LoggingEmailSender>();
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<IEmailSender>(provider =>
        {
            var email = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
            return string.Equals(email.Provider, "Smtp", StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<SmtpEmailSender>()
                : provider.GetRequiredService<LoggingEmailSender>();
        });

        // Identity email confirmation tokens are signed with the Data Protection API.
        // Keys default to the local filesystem. When running multiple instances
        // (containers, load-balanced replicas) you MUST configure a shared key ring,
        // e.g. Azure Key Vault, Redis, or a persistent shared volume, otherwise token
        // validation fails across instances.
        services.AddDataProtection()
            .SetApplicationName("Multitenant.Api");

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.UsesSecureBaseUrl(),
                "Auth:PublicBaseUrl must be an HTTPS URL, or plain HTTP on a loopback host for local development.")
            .ValidateOnStart();

        services.AddOptions<RefreshTokenCleanupOptions>()
            .Bind(configuration.GetSection(RefreshTokenCleanupOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<IdentityFeatureOptions>()
            .Bind(configuration.GetSection(IdentityFeatureOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => IsKnownEmailProvider(options.Provider),
                "Email:Provider must be 'Logging' or 'Smtp'.")
            .Validate(
                options => !string.Equals(options.Provider, "Smtp", StringComparison.OrdinalIgnoreCase)
                    || (options.Smtp is not null && !string.IsNullOrWhiteSpace(options.Smtp.Host)),
                "Email:Smtp:Host is required when Email:Provider is 'Smtp'.")
            .Validate(
                options => options.Smtp is not null && options.Smtp.Port is >= 1 and <= 65535,
                "Email:Smtp:Port must be between 1 and 65535.")
            .Validate(
                options => options.Smtp is not null
                    && string.IsNullOrWhiteSpace(options.Smtp.Username)
                        == string.IsNullOrWhiteSpace(options.Smtp.Password),
                "Email:Smtp:Username and Email:Smtp:Password must be configured together.")
            .ValidateOnStart();

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            // DataAnnotations does not recurse into the nested policy objects.
            .Validate(
                options => IsValidPolicy(options.Tenant) && IsValidPolicy(options.Auth),
                "RateLimiting permit limits must be 1–100,000 and window seconds must be 1–86,400.")
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<DemoOptions>()
            .Bind(configuration.GetSection(DemoOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .Validate(
                options => options.AllowedOrigins is not null
                    && options.AllowedOrigins.All(IsValidCorsOrigin),
                "Cors:AllowedOrigins must contain explicit HTTPS origins, or HTTP loopback origins for local development.")
            .ValidateOnStart();
    }

    private static bool IsValidPolicy(RateLimitPolicyOptions? policy) =>
        policy is not null
        && policy.PermitLimit is >= 1 and <= 100_000
        && policy.WindowSeconds is >= 1 and <= 86_400;

    private static bool IsKnownEmailProvider(string provider) =>
        string.Equals(provider, "Logging", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidCorsOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0);
}

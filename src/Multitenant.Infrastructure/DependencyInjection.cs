using Microsoft.AspNetCore.Authentication.JwtBearer;
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
                options.User.AllowedUserNameCharacters += ":";

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

        // Identity's own options are configured from the feature flags so the
        // behaviour of the API and the Identity primitives cannot drift apart.
        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<IdentityFeatureOptions>>((identity, features) =>
            {
                identity.SignIn.RequireConfirmedEmail = features.Value.RequireConfirmedEmail;
                identity.SignIn.RequireConfirmedAccount = features.Value.RequireConfirmedEmail;
                identity.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwt) => JwtBearerConfiguration.Configure(options, jwt.Value));

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<RefreshTokenManager>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantService, TenantService>();
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
            .ValidateOnStart();

        services.AddOptions<IdentityFeatureOptions>()
            .Bind(configuration.GetSection(IdentityFeatureOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => IsKnownEmailProvider(options.Provider),
                "Email:Provider must be 'Logging' or 'Smtp'.")
            .Validate(
                options => !string.Equals(options.Provider, "Smtp", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(options.Smtp.Host),
                "Email:Smtp:Host is required when Email:Provider is 'Smtp'.")
            .ValidateOnStart();

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddOptions<DemoOptions>()
            .Bind(configuration.GetSection(DemoOptions.SectionName));

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName));
    }

    private static bool IsKnownEmailProvider(string provider) =>
        string.Equals(provider, "Logging", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase);
}

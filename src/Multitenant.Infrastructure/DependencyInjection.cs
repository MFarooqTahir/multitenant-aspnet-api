using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Multitenant.Application.Abstractions;
using Multitenant.Application.Configuration;
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

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres is not configured. Set it in appsettings.json, user secrets or the ConnectionStrings__Postgres environment variable.");
        }

        services.AddScoped<TenantAuditingInterceptor>();
        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
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
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
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
        services.AddScoped<ITenantStore, TenantStore>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<RefreshTokenManager>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IUserService, UserService>();
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
}

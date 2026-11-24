using System.Net;
using Multitenant.Application.Contracts.Auth;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Contracts.Tenants;
using Multitenant.Application.Contracts.Users;
using Multitenant.Application.Common.Security;
using Multitenant.Domain.Entities;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed class ValidationBoundaryTests : IntegrationTestBase
{
    public ValidationBoundaryTests(IntegrationHarness harness) => Harness = harness;

    [Theory]
    [InlineData(11, HttpStatusCode.BadRequest)]
    [InlineData(12, HttpStatusCode.Created)]
    [InlineData(128, HttpStatusCode.Created)]
    [InlineData(129, HttpStatusCode.BadRequest)]
    public async Task OnboardingPassword_AtLengthBoundaries_ValidatesOrSucceeds(int length, HttpStatusCode expected)
    {
        using var client = CreateClient();
        var password = "Aa1!" + new string('a', length - 4);

        var response = await client.PostApi(
            "/api/v1/tenants/onboard",
            new OnboardTenantRequest($"password-{length}", "Password Boundary", $"password-{length}@acme.test", "Owner", password));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            await AssertValidationAsync(response, "ownerPassword");
        }
    }

    [Theory]
    [InlineData("title-min")]
    [InlineData("title-max")]
    [InlineData("description-max")]
    [InlineData("assignee-empty")]
    [InlineData("status-invalid")]
    [InlineData("priority-invalid")]
    public async Task TaskInputs_OutsideCapsOrEnums_ReturnFieldValidationProblem(string inputCase)
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"validation-task-{inputCase}", $"validation-task-{inputCase}@acme.test");
        var project = await Scenarios.CreateProjectAsync(client, tokens.AccessToken, "Validation project");
        object body = inputCase switch
        {
            "title-min" => new { title = "A", description = (string?)null, status = WorkItemStatus.Todo, priority = WorkItemPriority.Medium, assigneeUserId = (Guid?)null, dueDate = (DateOnly?)null },
            "title-max" => new { title = new string('a', 201), description = (string?)null, status = WorkItemStatus.Todo, priority = WorkItemPriority.Medium, assigneeUserId = (Guid?)null, dueDate = (DateOnly?)null },
            "description-max" => new { title = "Valid title", description = new string('a', 4001), status = WorkItemStatus.Todo, priority = WorkItemPriority.Medium, assigneeUserId = (Guid?)null, dueDate = (DateOnly?)null },
            "assignee-empty" => new { title = "Valid title", description = (string?)null, status = WorkItemStatus.Todo, priority = WorkItemPriority.Medium, assigneeUserId = Guid.Empty, dueDate = (DateOnly?)null },
            "status-invalid" => new { title = "Valid title", description = (string?)null, status = (WorkItemStatus)999, priority = WorkItemPriority.Medium, assigneeUserId = (Guid?)null, dueDate = (DateOnly?)null },
            _ => new { title = "Valid title", description = (string?)null, status = WorkItemStatus.Todo, priority = (WorkItemPriority)999, assigneeUserId = (Guid?)null, dueDate = (DateOnly?)null },
        };
        var field = inputCase switch
        {
            "title-min" or "title-max" => "title",
            "description-max" => "description",
            "assignee-empty" => "assigneeUserId",
            "status-invalid" => "status",
            _ => "priority",
        };

        var response = await client.PostApi($"/api/v1/projects/{project.Id}/tasks", body, tokens.AccessToken);

        await AssertValidationAsync(response, field);
    }

    [Theory]
    [InlineData("email-max")]
    [InlineData("display-min")]
    [InlineData("display-max")]
    [InlineData("password-max")]
    [InlineData("role-invalid")]
    public async Task UserInputs_OutsideCapsOrRole_ReturnFieldValidationProblem(string inputCase)
    {
        using var client = CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"validation-user-{inputCase}", $"validation-user-{inputCase}@acme.test");
        var email = inputCase == "email-max" ? new string('a', 250) + "@test.com" : $"valid-{inputCase}@acme.test";
        var displayName = inputCase switch
        {
            "display-min" => "A",
            "display-max" => new string('a', 81),
            _ => "Valid User",
        };
        var password = inputCase == "password-max" ? "Aa1!" + new string('a', 125) : Scenarios.Password;
        var role = inputCase == "role-invalid" ? "Superuser" : TenantRoles.Member;
        var field = inputCase switch
        {
            "email-max" => "email",
            "display-min" or "display-max" => "displayName",
            "password-max" => "password",
            _ => "role",
        };

        var response = await client.PostApi(
            "/api/v1/users",
            new CreateUserRequest(email, displayName, password, role),
            tokens.AccessToken);

        await AssertValidationAsync(response, field);
    }

    [Theory]
    [InlineData("/api/v1/projects")]
    [InlineData("/api/v1/tasks")]
    [InlineData("/api/v1/users")]
    public async Task SearchQueries_AboveCap_ReturnFieldValidationProblem(string resource)
    {
        using var client = CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"validation-search-{suffix}", $"validation-search-{suffix}@acme.test");

        var response = await client.GetApi($"{resource}?search={new string('a', 201)}", tokens.AccessToken);

        await AssertValidationAsync(response, "search");
    }

    [Fact]
    public async Task RefreshToken_AtAndAboveCap_DistinguishesUnauthorizedFromValidation()
    {
        using var client = CreateClient();

        var atCap = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(new string('a', 512)));
        var aboveCap = await client.PostApi(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(new string('a', 513)));

        Assert.Equal(HttpStatusCode.Unauthorized, atCap.StatusCode);
        await AssertValidationAsync(aboveCap, "refreshToken");
    }

    [Theory]
    [InlineData("token-max", "twoFactorToken")]
    [InlineData("code-max", "code")]
    [InlineData("five-digits", "code")]
    [InlineData("seven-digits", "code")]
    [InlineData("non-ascii-digits", "code")]
    [InlineData("both-present", "request")]
    [InlineData("recovery-max", "recoveryCode")]
    [InlineData("missing-code", "request")]
    [InlineData("enable-code-max", "code")]
    [InlineData("enable-non-digits", "code")]
    [InlineData("disable-code-max", "code")]
    [InlineData("disable-non-digits", "code")]
    public async Task TwoFactorInputs_OutsideCapsOrMissingCode_ReturnFieldValidationProblem(string inputCase, string field)
    {
        await using var factory = Harness.CreateFactory(configuration =>
            configuration["Identity:EnableTwoFactor"] = "true");
        using var client = factory.CreateClient();
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"validation-2fa-{inputCase}", $"validation-2fa-{inputCase}@acme.test");
        (string path, object body) = inputCase switch
        {
            "token-max" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = new string('a', 4097), code = "123456", recoveryCode = (string?)null }),
            "code-max" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = "token", code = new string('1', 11), recoveryCode = (string?)null }),
            "five-digits" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = "token", code = "12345", recoveryCode = (string?)null }),
            "seven-digits" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = "token", code = "1234567", recoveryCode = (string?)null }),
            "non-ascii-digits" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = "token", code = "١٢٣٤٥٦", recoveryCode = (string?)null }),
            "both-present" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = "token", code = "123456", recoveryCode = "recovery" }),
            "recovery-max" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = "token", code = (string?)null, recoveryCode = new string('a', 129) }),
            "missing-code" => ("/api/v1/auth/2fa/verify", (object)new { twoFactorToken = "token", code = (string?)null, recoveryCode = (string?)null }),
            "enable-code-max" => ("/api/v1/auth/2fa/enable", (object)new { code = new string('1', 33) }),
            "enable-non-digits" => ("/api/v1/auth/2fa/enable", (object)new { code = "abcdef" }),
            "disable-code-max" => ("/api/v1/auth/2fa/disable", (object)new { code = new string('1', 33) }),
            _ => ("/api/v1/auth/2fa/disable", (object)new { code = "abcdef" }),
        };

        var response = await client.PostApi(path, body, tokens.AccessToken);

        await AssertValidationAsync(response, field);
    }

    [Theory]
    [InlineData("/api/v1/projects?status=NotAStatus", "status")]
    [InlineData("/api/v1/tasks?status=NotAStatus", "status")]
    [InlineData("/api/v1/tasks?priority=NotAPriority", "priority")]
    [InlineData("/api/v1/tasks?dueBefore=not-a-date", "dueBefore")]
    [InlineData("/api/v1/tasks?dueAfter=not-a-date", "dueAfter")]
    [InlineData("/api/v1/tasks?dueBefore=2026-11-01&dueAfter=2026-11-02", "request")]
    [InlineData("/api/v1/users?role=Superuser", "role")]
    [InlineData("/api/v1/users?isActive=not-a-bool", "isActive")]
    public async Task QueryEnumDateAndBool_InvalidValuesReturnFieldValidationProblem(string query, string field)
    {
        using var client = CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tokens = await Scenarios.OnboardAndLoginAsync(client, $"validation-query-{suffix}", $"validation-query-{suffix}@acme.test");

        var response = await client.GetApi(query, tokens.AccessToken);

        await AssertValidationAsync(response, field);
    }

    [Fact]
    public void PasswordRules_DescribeTheEnforcedTwelveToOneHundredTwentyEightRange()
    {
        Assert.Equal(12, PasswordRules.MinimumLength);
        Assert.Equal(128, PasswordRules.MaximumLength);
        Assert.Contains("12-128", PasswordRules.Description, StringComparison.Ordinal);
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.Equal("validation_failed", problem.Extensions["code"]?.ToString());
        Assert.Contains(problem.Errors.Keys, key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
    }
}

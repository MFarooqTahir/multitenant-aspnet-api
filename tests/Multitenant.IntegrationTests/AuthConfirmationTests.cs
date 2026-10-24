using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Multitenant.Application.Abstractions.Messaging;
using Multitenant.Application.Contracts.Auth;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public sealed partial class AuthConfirmationTests : IntegrationTestBase
{
    public AuthConfirmationTests(IntegrationHarness harness) => Harness = harness;

    [Fact]
    public async Task ConfirmationLinkWorksEvenWhenConfirmationIsOptional()
    {
        var sender = new CapturingEmailSender();
        await using var factory = CreateFactoryWithEmailCapture(sender);
        using var client = factory.CreateClient();

        await Scenarios.OnboardAsync(client, "confirm-off", "Confirm Off", "confirm-off@acme.test");

        var message = Assert.Single(sender.Messages);
        var (userId, token) = ExtractConfirmationLink(message.TextBody);

        var confirm = await client.PostApi(
            "/api/v1/auth/confirm-email",
            new ConfirmEmailRequest(userId, token));

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
    }

    [Fact]
    public async Task ConfirmationIsRequiredForLoginWhenTheFlagIsOn()
    {
        var sender = new CapturingEmailSender();
        await using var factory = Harness.CreateFactory(
            configuration => configuration["Identity:RequireConfirmedEmail"] = "true",
            services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        using var client = factory.CreateClient();

        await Scenarios.OnboardAsync(client, "confirm-on", "Confirm On", "confirm-on@acme.test");

        var blocked = await client.PostApi(
            "/api/v1/auth/login",
            new LoginRequest("confirm-on", "confirm-on@acme.test", Scenarios.Password));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        var message = Assert.Single(sender.Messages);
        var (userId, token) = ExtractConfirmationLink(message.TextBody);

        var confirm = await client.PostApi(
            "/api/v1/auth/confirm-email",
            new ConfirmEmailRequest(userId, token));
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var login = await Scenarios.LoginAsync(client, "confirm-on", "confirm-on@acme.test", Scenarios.Password);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
    }

    [Fact]
    public async Task ResendIsAcceptedButSendsNothingWhenConfirmationIsOptional()
    {
        var sender = new CapturingEmailSender();
        await using var factory = CreateFactoryWithEmailCapture(sender);
        using var client = factory.CreateClient();

        await Scenarios.OnboardAsync(client, "confirm-noop", "Confirm NoOp", "confirm-noop@acme.test");
        sender.Clear();

        var resend = await client.PostApi(
            "/api/v1/auth/resend-confirmation",
            new ResendConfirmationRequest("confirm-noop", "confirm-noop@acme.test"));

        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Empty(sender.Messages);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Multitenant.Api.IApiMarker> CreateFactoryWithEmailCapture(
        CapturingEmailSender sender) =>
        Harness.CreateFactory(
            configureServices: services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });

    private static (Guid UserId, string Token) ExtractConfirmationLink(string body)
    {
        var match = ConfirmationLinkPattern().Match(body);
        Assert.True(match.Success, $"No confirmation link found in '{body}'.");

        return (
            Guid.Parse(match.Groups["userId"].Value),
            Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"userId=(?<userId>[0-9a-fA-F-]+)&token=(?<token>[^&\s]+)")]
    private static partial Regex ConfirmationLinkPattern();
}

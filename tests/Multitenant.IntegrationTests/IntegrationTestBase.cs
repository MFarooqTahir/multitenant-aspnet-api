using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Multitenant.IntegrationTests.Infrastructure;

namespace Multitenant.IntegrationTests;

[Collection(IntegrationHarness.CollectionName)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private static readonly IntegrationHarness Harness = new();

    public Task InitializeAsync() => Harness.InitializeAsync();

    public Task DisposeAsync() => Harness.DisposeAsync();

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    protected static HttpClient CreateClient() => IntegrationHarness.DefaultFactory.CreateClient();

    protected static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    protected static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? bearer = null) =>
        SendAsync(client, HttpMethod.Get, path, bearer);

    protected static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body, string? bearer = null) =>
        SendAsync(client, HttpMethod.Post, path, body, bearer);

    protected static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body, string? bearer = null) =>
        SendAsync(client, HttpMethod.Put, path, body, bearer);

    protected static Task<HttpResponseMessage> PatchAsync(HttpClient client, string path, object body, string? bearer = null) =>
        SendAsync(client, HttpMethod.Patch, path, body, bearer);

    protected static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path, object? body = null, string? bearer = null) =>
        SendAsync(client, HttpMethod.Delete, path, body, bearer);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null,
        string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Content = body is null ? null : JsonContent(body);

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await client.SendAsync(request);
    }
}

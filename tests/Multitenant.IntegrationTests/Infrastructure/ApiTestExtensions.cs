using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Multitenant.IntegrationTests.Infrastructure;

/// <summary>
/// Small HTTP helpers so tests read as request/response stories instead of
/// boilerplate. All requests target the real pipeline through <see cref="HttpClient"/>.
/// </summary>
public static class ApiTestExtensions
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> GetApi(this HttpClient client, string path, string? bearer = null) =>
        SendAsync(client, HttpMethod.Get, path, null, bearer);

    public static Task<HttpResponseMessage> PostApi(
        this HttpClient client,
        string path,
        object? body,
        string? bearer = null) =>
        SendAsync(client, HttpMethod.Post, path, body, bearer);

    public static Task<HttpResponseMessage> PutApi(
        this HttpClient client,
        string path,
        object body,
        string? bearer = null) =>
        SendAsync(client, HttpMethod.Put, path, body, bearer);

    public static Task<HttpResponseMessage> PatchApi(
        this HttpClient client,
        string path,
        object body,
        string? bearer = null) =>
        SendAsync(client, HttpMethod.Patch, path, body, bearer);

    public static Task<HttpResponseMessage> DeleteApi(
        this HttpClient client,
        string path,
        object? body = null,
        string? bearer = null) =>
        SendAsync(client, HttpMethod.Delete, path, body, bearer);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        return value ?? throw new InvalidOperationException($"Response body for {typeof(T).Name} was empty.");
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string? bearer)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : JsonContent(body),
        };

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await client.SendAsync(request);
    }
}

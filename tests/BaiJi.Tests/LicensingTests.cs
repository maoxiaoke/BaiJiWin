using System.Net;
using BaiJi.Core;
using Xunit;

namespace BaiJi.Tests;

public class LicensingTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public readonly List<string> RequestBodies = new();
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null) RequestBodies.Add(await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static (LemonSqueezyClient, StubHandler) Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        return (new LemonSqueezyClient(new HttpClient(handler)), handler);
    }

    private const string ValidKey = "0123456789-ABCDEFGHIJKLMNO"; // >= 25 chars

    [Fact]
    public async Task Short_key_is_rejected_without_a_request()
    {
        var (client, handler) = Client(_ => Json("{}"));
        var result = await client.ActivateAsync("too-short");
        Assert.False(result.Success);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task Successful_activation_extracts_key_instance_and_expiry()
    {
        var (client, handler) = Client(_ => Json(
            """{"activated":true,"license_key":{"key":"REALKEY","expires_at":"2030-01-01"},"instance":{"id":"inst-1"}}"""));
        var result = await client.ActivateAsync(ValidKey);

        Assert.True(result.Success);
        Assert.Equal("REALKEY", result.LicenseKey);
        Assert.Equal("inst-1", result.InstanceId);
        Assert.Equal("2030-01-01", result.ExpiryDate);
        Assert.Contains("license_key=", handler.RequestBodies[0]);
        Assert.Contains("instance_name=BaiJi", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Activation_without_expiry_defaults_to_indefinite()
    {
        var (client, _) = Client(_ => Json("""{"activated":true,"license_key":{"key":"K"},"instance":{"id":"i"}}"""));
        var result = await client.ActivateAsync(ValidKey);
        Assert.True(result.Success);
        Assert.Equal("Indefinite", result.ExpiryDate);
    }

    [Fact]
    public async Task Server_rejection_surfaces_the_error_message()
    {
        var (client, _) = Client(_ => Json("""{"activated":false,"error":"license limit reached"}"""));
        var result = await client.ActivateAsync(ValidKey);
        Assert.False(result.Success);
        Assert.Equal("license limit reached", result.ErrorMessage);
    }

    [Fact]
    public async Task Network_failure_is_caught_and_reported()
    {
        var (client, _) = Client(_ => throw new HttpRequestException("offline"));
        var result = await client.ActivateAsync(ValidKey);
        Assert.False(result.Success);
        Assert.Contains("offline", result.ErrorMessage);
    }

    [Fact]
    public async Task Deactivation_succeeds_and_sends_instance_id()
    {
        var (client, handler) = Client(_ => Json("""{"deactivated":true}"""));
        var result = await client.DeactivateAsync(ValidKey, "inst-1");
        Assert.True(result.Success);
        Assert.Contains("instance_id=inst-1", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Manager_persists_state_on_activation_and_clears_on_deactivation()
    {
        var store = new InMemorySettingsStore();
        var (client, _) = Client(req =>
            req.RequestUri!.AbsoluteUri.Contains("deactivate")
                ? Json("""{"deactivated":true}""")
                : Json("""{"activated":true,"license_key":{"key":"K","expires_at":"2031-05-05"},"instance":{"id":"i9"}}"""));
        var manager = new LicenseManager(store, client);

        var statusEvents = 0;
        manager.StatusChanged += () => statusEvents++;

        Assert.False(manager.IsActive);
        var activation = await manager.ActivateAsync(ValidKey);
        Assert.True(activation.Success);
        Assert.True(manager.IsActive);
        Assert.Equal("K", manager.LicenseKey);
        Assert.Equal("2031-05-05", manager.ExpiryDate);

        var deactivation = await manager.DeactivateAsync();
        Assert.True(deactivation.Success);
        Assert.False(manager.IsActive);
        Assert.Equal("N/A", manager.LicenseKey);
        Assert.Equal(2, statusEvents);
    }

    [Fact]
    public async Task Deactivation_without_stored_license_fails_fast()
    {
        var store = new InMemorySettingsStore();
        var (client, handler) = Client(_ => Json("""{"deactivated":true}"""));
        var manager = new LicenseManager(store, client);
        var result = await manager.DeactivateAsync();
        Assert.False(result.Success);
        Assert.Empty(handler.RequestBodies);
    }
}

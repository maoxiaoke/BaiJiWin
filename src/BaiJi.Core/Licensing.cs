using System.Text.Json;

namespace BaiJi.Core;

public readonly record struct ActivationResult(bool Success, string? ErrorMessage, string? LicenseKey, string? InstanceId, string? ExpiryDate);
public readonly record struct DeactivationResult(bool Success, string? ErrorMessage);

/// <summary>
/// Talks to LemonSqueezy's license API — the same endpoints the macOS app uses
/// (<c>/v1/licenses/activate</c> and <c>/v1/licenses/deactivate</c>). The
/// <see cref="HttpClient"/> is injectable so tests supply a stub handler.
/// </summary>
public sealed class LemonSqueezyClient
{
    private readonly HttpClient _http;

    public const string BuyUrl = "https://anotherme.lemonsqueezy.com/buy/e8dc0135-2694-41ce-90e7-cc9a11720dc6";
    public const string ManageUrl = "https://app.lemonsqueezy.com/my-orders";
    private const string ActivateUrl = "https://api.lemonsqueezy.com/v1/licenses/activate";
    private const string DeactivateUrl = "https://api.lemonsqueezy.com/v1/licenses/deactivate";

    public LemonSqueezyClient(HttpClient http) => _http = http;

    public async Task<ActivationResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
    {
        if (licenseKey.Length < 25)
            return new ActivationResult(false, "Invalid license key", null, null, null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ActivateUrl);
            request.Headers.Add("Accept", "application/json");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"] = licenseKey,
                ["instance_name"] = "BaiJi",
            });

            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("activated", out var activatedEl) && activatedEl.GetBoolean())
            {
                var keyInfo = root.GetProperty("license_key");
                var key = keyInfo.GetProperty("key").GetString();
                var instanceId = root.GetProperty("instance").GetProperty("id").GetString();
                if (key is null || instanceId is null)
                    return new ActivationResult(false, "Failed to extract license information", null, null, null);

                var expiry = keyInfo.TryGetProperty("expires_at", out var exp) && exp.ValueKind == JsonValueKind.String
                    ? exp.GetString()
                    : "Indefinite";
                return new ActivationResult(true, null, key, instanceId, expiry);
            }

            var error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : null;
            return new ActivationResult(false, error ?? "Activation failed", null, null, null);
        }
        catch (Exception ex)
        {
            return new ActivationResult(false, $"Activation failed: {ex.Message}", null, null, null);
        }
    }

    public async Task<DeactivationResult> DeactivateAsync(string licenseKey, string instanceId, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, DeactivateUrl);
            request.Headers.Add("Accept", "application/json");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["license_key"] = licenseKey,
                ["instance_id"] = instanceId,
            });

            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("deactivated", out var deEl) && deEl.GetBoolean())
                return new DeactivationResult(true, null);

            var error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : null;
            return new DeactivationResult(false, error ?? "Deactivation failed");
        }
        catch (Exception ex)
        {
            return new DeactivationResult(false, $"Deactivation failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Persists and exposes license state. Mirrors the macOS LicenseManager: a
/// simple "Active"/"Inactive" status string plus the stored key/instance/expiry.
/// </summary>
public sealed class LicenseManager
{
    private readonly ISettingsStore _store;
    private readonly LemonSqueezyClient _client;

    public const string ActiveStatus = "Active";
    public const string InactiveStatus = "Inactive";

    public event Action? StatusChanged;

    public LicenseManager(ISettingsStore store, LemonSqueezyClient client)
    {
        _store = store;
        _client = client;
    }

    public string LicenseStatus => _store.GetString("licenseStatus") ?? InactiveStatus;
    public bool IsActive => LicenseStatus == ActiveStatus;
    public string LicenseKey => _store.GetString("licenseKey") ?? "N/A";
    public string ExpiryDate => _store.GetString("licenseExpiryDate") ?? "Indefinite";

    public async Task<ActivationResult> ActivateAsync(string licenseKey, CancellationToken ct = default)
    {
        var result = await _client.ActivateAsync(licenseKey, ct).ConfigureAwait(false);
        if (result.Success)
        {
            _store.SetString("licenseKey", result.LicenseKey!);
            _store.SetString("licenseStatus", ActiveStatus);
            _store.SetString("instanceId", result.InstanceId!);
            _store.SetString("licenseExpiryDate", result.ExpiryDate ?? "Indefinite");
            StatusChanged?.Invoke();
        }
        return result;
    }

    public async Task<DeactivationResult> DeactivateAsync(CancellationToken ct = default)
    {
        var key = _store.GetString("licenseKey");
        var instanceId = _store.GetString("instanceId");
        if (key is null || instanceId is null)
            return new DeactivationResult(false, "No active license found");

        var result = await _client.DeactivateAsync(key, instanceId, ct).ConfigureAwait(false);
        if (result.Success) ClearLicenseData();
        return result;
    }

    public void ClearLicenseData()
    {
        _store.Remove("licenseKey");
        _store.SetString("licenseStatus", InactiveStatus);
        _store.Remove("licenseExpiryDate");
        _store.Remove("instanceId");
        StatusChanged?.Invoke();
    }
}

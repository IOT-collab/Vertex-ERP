using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VertexERP.Models;

namespace VertexERP.Services;

public interface IShipmentTrackingService
{
    bool IsConfigured { get; }
    Task<ShipmentTrackingServiceResult> TrackAsync(string trackingNumber, CancellationToken cancellationToken = default);
}

public sealed class DtdcTrackingService : IShipmentTrackingService
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DtdcTrackingService> _logger;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Dtdc:ApiKey"]);

    public DtdcTrackingService(HttpClient client, IConfiguration configuration, ILogger<DtdcTrackingService> logger)
    { _client = client; _configuration = configuration; _logger = logger; }

    public async Task<ShipmentTrackingServiceResult> TrackAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Dtdc:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return new(false, null, "DTDC tracking API key is not configured yet.");
        var cleanNumber = trackingNumber.Trim().ToUpperInvariant();
        if (cleanNumber.Length is < 4 or > 100 || cleanNumber.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            return new(false, null, "Enter a valid DTDC AWB / consignment number.");

        var baseUrl = (_configuration["Dtdc:TrackingUrl"] ?? "https://api.trackingmore.com/v4").TrimEnd('/');
        try
        {
            using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/trackings/create");
            createRequest.Headers.TryAddWithoutValidation("Tracking-Api-Key", apiKey.Trim());
            createRequest.Content = JsonContent.Create(new { tracking_number = cleanNumber, courier_code = "dtdc", title = $"DTDC {cleanNumber}" });
            using var createResponse = await _client.SendAsync(createRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var json = await createResponse.Content.ReadAsStringAsync(cancellationToken);

            if (createResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(false, null, "DTDC tracking authentication failed. Please verify Dtdc:ApiKey.");

            // TrackingMore returns a business error when an AWB already exists.
            // Read the stored result in that case instead of failing the user request.
            if (createResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest)
            {
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/trackings/get?courier_code=dtdc&tracking_numbers={Uri.EscapeDataString(cleanNumber)}&items_amount=1&pages_amount=1");
                getRequest.Headers.TryAddWithoutValidation("Tracking-Api-Key", apiKey.Trim());
                using var getResponse = await _client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (getResponse.IsSuccessStatusCode) json = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                else if (!createResponse.IsSuccessStatusCode) return new(false, null, ReadApiError(json) ?? "No DTDC shipment was found for this AWB number.");
            }
            else if (!createResponse.IsSuccessStatusCode)
            {
                return new(false, null, ReadApiError(json) ?? $"DTDC tracking could not process this request ({(int)createResponse.StatusCode}).");
            }
            return Parse(json, cleanNumber);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, null, "DTDC tracking took too long to respond. Please try again."); }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "DTDC tracking request failed for {TrackingNumber}", cleanNumber);
            return new(false, null, "Unable to connect to the DTDC tracking service right now.");
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "DTDC tracking returned invalid JSON for {TrackingNumber}", cleanNumber);
            return new(false, null, "DTDC tracking returned an unexpected response.");
        }
    }

    private static ShipmentTrackingServiceResult Parse(string json, string fallbackNumber)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (TryGet(root, "meta", out var meta))
        {
            var apiCode = Read(meta, "code");
            if (!string.IsNullOrWhiteSpace(apiCode) && apiCode != "200")
                return new(false, null, Read(meta, "message") ?? "TrackingMore could not retrieve this DTDC shipment.");
        }
        if (TryGet(root, "data", out var data)) root = data;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0) return new(false, null, "No DTDC shipment was found for this AWB number.");
            root = root[0];
        }

        var scans = new List<ShipmentScanItem>();
        var checkpointItems = FindTrackingEvents(root).ToList();
        foreach (var checkpoint in checkpointItems)
        {
            scans.Add(new ShipmentScanItem
            {
                Status = Read(checkpoint, "checkpoint_delivery_status", "delivery_status", "status", "StatusDescription") ?? "Shipment update",
                Location = Read(checkpoint, "location", "checkpoint_location", "city", "Details") ?? string.Empty,
                Instructions = Read(checkpoint, "tracking_detail", "message", "description", "status_info") ?? string.Empty,
                DateTime = ReadDate(checkpoint, "checkpoint_date", "checkpoint_time", "event_time", "created_at", "Date")
            });
        }
        scans = scans.OrderByDescending(item => item.DateTime).ToList();
        var latest = checkpointItems.OrderByDescending(item => ReadDate(item, "checkpoint_date", "checkpoint_time", "event_time", "created_at", "Date")).FirstOrDefault();
        var status = Read(root, "delivery_status", "status") ?? Read(latest, "checkpoint_delivery_status", "delivery_status", "status") ?? "Pending";
        var currentLocation = Read(latest, "location", "checkpoint_location", "city", "Details") ?? Read(root, "current_location") ?? string.Empty;
        return new(true, new ShipmentTrackingResult
        {
            Waybill = Read(root, "tracking_number", "number") ?? fallbackNumber,
            ReferenceNumber = Read(root, "order_number", "order_id", "title") ?? string.Empty,
            CurrentStatus = Humanize(status),
            StatusType = Read(root, "delivery_status") ?? string.Empty,
            CurrentLocation = currentLocation,
            Instructions = Read(latest, "tracking_detail", "message", "description") ?? string.Empty,
            StatusDateTime = ReadDate(root, "update_at", "updated_at") ?? ReadDate(latest, "checkpoint_date", "checkpoint_time", "event_time"),
            PickupDate = ReadDate(root, "tracking_ship_date", "created_at"),
            Origin = JoinLocation(root, "origin_city", "origin_state", "origin_country"),
            Destination = JoinLocation(root, "destination_city", "destination_state", "destination_country"),
            Consignee = Read(root, "customer_name") ?? string.Empty,
            Scans = scans
        }, null);
    }

    private static IEnumerable<JsonElement> FindTrackingEvents(JsonElement root)
    {
        foreach (var name in new[] { "checkpoints", "trackinfo", "tracking_details" })
            if (TryGet(root, name, out var direct) && direct.ValueKind == JsonValueKind.Array)
                foreach (var item in direct.EnumerateArray()) yield return item;

        foreach (var sectionName in new[] { "origin_info", "destination_info" })
            if (TryGet(root, sectionName, out var section) && section.ValueKind == JsonValueKind.Object)
                foreach (var item in FindTrackingEvents(section)) yield return item;
    }

    private static string JoinLocation(JsonElement root, params string[] names) =>
        string.Join(", ", names.Select(name => Read(root, name)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string? ReadApiError(string json)
    {
        try { using var document = JsonDocument.Parse(json); return FindString(document.RootElement, "message", "error_message", "type"); }
        catch (JsonException) { return null; }
    }
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default; if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        return false;
    }
    private static string? Read(JsonElement element, params string[] names)
    { foreach (var name in names) if (TryGet(element, name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number) return value.ToString(); return null; }
    private static string? FindString(JsonElement element, params string[] names)
    {
        var direct = Read(element, names); if (!string.IsNullOrWhiteSpace(direct)) return direct;
        if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) { var nested = FindString(property.Value, names); if (!string.IsNullOrWhiteSpace(nested)) return nested; }
        return null;
    }
    private static DateTime? ReadDate(JsonElement element, params string[] names) => DateTime.TryParse(Read(element, names), out var value) ? value : null;
    private static string Humanize(string value) => string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString())).Replace('_', ' ').Trim();
}

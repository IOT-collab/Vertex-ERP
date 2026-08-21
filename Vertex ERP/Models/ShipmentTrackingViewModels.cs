using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class ShipmentTrackingPageViewModel
{
    [Required, StringLength(100)]
    [Display(Name = "Waybill / Tracking Number")]
    public string TrackingNumber { get; set; } = string.Empty;
    public ShipmentTrackingResult? Shipment { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsApiConfigured { get; set; }
}

public sealed class ShipmentTrackingResult
{
    public string Waybill { get; init; } = string.Empty;
    public string ReferenceNumber { get; init; } = string.Empty;
    public string CurrentStatus { get; init; } = "Unknown";
    public string StatusType { get; init; } = string.Empty;
    public string CurrentLocation { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public DateTime? StatusDateTime { get; init; }
    public DateTime? PickupDate { get; init; }
    public string Origin { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public string Consignee { get; init; } = string.Empty;
    public IReadOnlyList<ShipmentScanItem> Scans { get; init; } = Array.Empty<ShipmentScanItem>();
}

public sealed class ShipmentScanItem
{
    public string Status { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public DateTime? DateTime { get; init; }
}

public sealed record ShipmentTrackingServiceResult(bool Success, ShipmentTrackingResult? Shipment, string? ErrorMessage);

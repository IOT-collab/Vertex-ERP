using Microsoft.Extensions.Options;
using VertexERP.Models;
using VertexERP.Repositories;

namespace VertexERP.Services;

public sealed class BiometricDeviceService : IBiometricDeviceService
{
    private readonly IBiometricRepository _repository;
    private readonly AttendanceOptions _options;
    private readonly ILogger<BiometricDeviceService> _logger;

    public BiometricDeviceService(IBiometricRepository repository, IOptions<AttendanceOptions> options, ILogger<BiometricDeviceService> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BiometricDeviceListItemViewModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _repository.GetDevicesAsync(cancellationToken);
        var onlineAfter = DateTime.UtcNow.AddMinutes(-_options.OnlineThresholdMinutes);
        return devices.Select(device => new BiometricDeviceListItemViewModel
        {
            Id = device.Id, Name = device.Name, SerialNumber = device.SerialNumber, Model = device.Model,
            BranchCode = string.IsNullOrWhiteSpace(device.BranchCode) ? "—" : device.BranchCode,
            Endpoint = string.IsNullOrWhiteSpace(device.ServerAddress) ? "—" : $"{device.ServerAddress}:{device.ServerPort}",
            IsActive = device.IsActive, IsOnline = device.IsActive && device.LastSeenUtc >= onlineAfter,
            LastSeenUtc = device.LastSeenUtc, LastSyncUtc = device.LastSyncUtc, MappingCount = device.EmployeeMappings.Count(mapping => mapping.IsActive)
        }).ToList();
    }

    public Task<BiometricDevice?> GetAsync(int id, CancellationToken cancellationToken = default) => _repository.GetDeviceAsync(id, cancellationToken);

    public async Task<int> CreateAsync(BiometricDeviceFormViewModel model, CancellationToken cancellationToken = default)
    {
        var serial = NormalizeSerial(model.SerialNumber);
        if (await _repository.GetDeviceBySerialAsync(serial, cancellationToken) is not null) throw new InvalidOperationException("A device with this serial number already exists.");
        var device = new BiometricDevice(); Map(model, device); device.SerialNumber = serial;
        await _repository.AddDeviceAsync(device, cancellationToken); await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Biometric device {DeviceId} ({SerialNumber}) created", device.Id, device.SerialNumber);
        return device.Id;
    }

    public async Task<bool> UpdateAsync(BiometricDeviceFormViewModel model, CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetDeviceAsync(model.Id, cancellationToken); if (device is null) return false;
        var serial = NormalizeSerial(model.SerialNumber);
        var duplicate = await _repository.GetDeviceBySerialAsync(serial, cancellationToken);
        if (duplicate is not null && duplicate.Id != device.Id) throw new InvalidOperationException("A device with this serial number already exists.");
        Map(model, device); device.SerialNumber = serial; device.UpdatedAtUtc = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken); return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetDeviceAsync(id, cancellationToken); if (device is null) return false;
        if (await _repository.DeviceHasAttendanceAsync(id, cancellationToken)) throw new InvalidOperationException("This device has attendance history and cannot be deleted. Mark it inactive instead.");
        _repository.RemoveDevice(device); await _repository.SaveChangesAsync(cancellationToken); return true;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(int id, CancellationToken cancellationToken = default)
    {
        var device = await _repository.GetDeviceAsync(id, cancellationToken);
        if (device is null) return (false, "Device was not found.");
        if (!device.IsActive) return (false, "Device is inactive.");
        if (!device.LastSeenUtc.HasValue) return (false, "No ADMS request has been received from this device yet.");
        var age = DateTime.UtcNow - device.LastSeenUtc.Value;
        return age.TotalMinutes <= _options.OnlineThresholdMinutes
            ? (true, $"Connected. Last device request was received {Math.Max(0, (int)age.TotalMinutes)} minute(s) ago.")
            : (false, $"Device is registered but offline. Last seen {device.LastSeenUtc.Value:dd MMM yyyy HH:mm} UTC.");
    }

    private static string NormalizeSerial(string serial) => serial.Trim().ToUpperInvariant();
    private static void Map(BiometricDeviceFormViewModel model, BiometricDevice device)
    {
        device.Name = model.Name.Trim(); device.Model = model.Model.Trim(); device.BranchCode = model.BranchCode?.Trim();
        device.ServerAddress = model.ServerAddress?.Trim(); device.ServerPort = model.ServerPort; device.CommunicationMode = model.CommunicationMode.Trim().ToUpperInvariant();
        device.FirmwareVersion = model.FirmwareVersion?.Trim(); device.IsActive = model.IsActive; device.Notes = model.Notes?.Trim();
    }
}

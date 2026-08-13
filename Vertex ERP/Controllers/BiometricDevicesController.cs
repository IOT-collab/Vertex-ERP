using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Repositories;
using VertexERP.Services;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VertexERP.Controllers;

[Authorize(Roles = "Admin,HR")]
public class BiometricDevicesController : Controller
{
    private readonly IBiometricDeviceService _deviceService;
    private readonly IBiometricRepository _repository;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<BiometricDevicesController> _logger;
    public BiometricDevicesController(IBiometricDeviceService deviceService, IBiometricRepository repository, ApplicationDbContext db, ILogger<BiometricDevicesController> logger)
    { _deviceService = deviceService; _repository = repository; _db = db; _logger = logger; }

    public async Task<IActionResult> Index(CancellationToken cancellationToken) => View(await _deviceService.GetAllAsync(cancellationToken));
    public IActionResult Setup()
    {
        ViewBag.ServerAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(item.Address))
            .Select(item => item.Address.ToString()).Distinct().OrderBy(address => address).ToList();
        return View();
    }
    public IActionResult Create() => View("Form", new BiometricDeviceFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BiometricDeviceFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Form", form);
        try { await _deviceService.CreateAsync(form, cancellationToken); TempData["BiometricMessage"] = "Biometric device added successfully."; return RedirectToAction(nameof(Index)); }
        catch (InvalidOperationException exception) { ModelState.AddModelError(nameof(form.SerialNumber), exception.Message); return View("Form", form); }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var device = await _deviceService.GetAsync(id, cancellationToken); if (device is null) return NotFound();
        return View("Form", new BiometricDeviceFormViewModel { Id = device.Id, Name = device.Name, SerialNumber = device.SerialNumber, Model = device.Model, BranchCode = device.BranchCode, ServerAddress = device.ServerAddress, ServerPort = device.ServerPort, CommunicationMode = device.CommunicationMode, FirmwareVersion = device.FirmwareVersion, IsActive = device.IsActive, Notes = device.Notes });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(BiometricDeviceFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Form", form);
        try { if (!await _deviceService.UpdateAsync(form, cancellationToken)) return NotFound(); TempData["BiometricMessage"] = "Device updated successfully."; return RedirectToAction(nameof(Index)); }
        catch (InvalidOperationException exception) { ModelState.AddModelError(nameof(form.SerialNumber), exception.Message); return View("Form", form); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try { if (!await _deviceService.DeleteAsync(id, cancellationToken)) return NotFound(); TempData["BiometricMessage"] = "Device deleted."; }
        catch (InvalidOperationException exception) { TempData["BiometricError"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(int id, CancellationToken cancellationToken)
    {
        var result = await _deviceService.TestConnectionAsync(id, cancellationToken); TempData[result.Success ? "BiometricMessage" : "BiometricError"] = result.Message; return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Mappings(int id, CancellationToken cancellationToken)
    {
        var device = await _deviceService.GetAsync(id, cancellationToken); if (device is null) return NotFound();
        ViewBag.Device = device; ViewBag.Employees = await _db.Employees.AsNoTracking().Where(employee => employee.IsActive).OrderBy(employee => employee.FullName).ToListAsync(cancellationToken);
        ViewBag.UnmappedUserIds = await _db.AttendanceLogs.AsNoTracking()
            .Where(log => log.BiometricDeviceId == id && log.EmployeeId == null)
            .GroupBy(log => log.DeviceUserId)
            .Select(group => new UnmappedDeviceUserViewModel { DeviceUserId = group.Key, PunchCount = group.Count(), LastPunch = group.Max(log => log.PunchTime) })
            .OrderByDescending(item => item.LastPunch).ToListAsync(cancellationToken);
        return View(await _repository.GetMappingsAsync(id, cancellationToken));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMapping(EmployeeDeviceMappingViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { TempData["BiometricError"] = "Employee and Device User ID are required."; return RedirectToAction(nameof(Mappings), new { id = model.DeviceId }); }
        try
        {
            var deviceUserId = model.DeviceUserId.Trim();
            await _repository.AddOrUpdateMappingAsync(new EmployeeDeviceMapping { BiometricDeviceId = model.DeviceId, EmployeeId = model.EmployeeId!.Value, DeviceUserId = deviceUserId, IsActive = true }, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            await _db.AttendanceLogs.Where(log => log.BiometricDeviceId == model.DeviceId && log.DeviceUserId == deviceUserId && log.EmployeeId == null).ExecuteUpdateAsync(update => update.SetProperty(log => log.EmployeeId, model.EmployeeId.Value), cancellationToken);
            TempData["BiometricMessage"] = "Employee mapping saved. Existing raw punches were linked automatically.";
        }
        catch (DbUpdateException exception) { _logger.LogWarning(exception, "Could not save biometric mapping"); TempData["BiometricError"] = "This employee or device user ID is already mapped."; }
        return RedirectToAction(nameof(Mappings), new { id = model.DeviceId });
    }
}

using VertexERP.Models;
namespace VertexERP.Services;
public static class SiteLocationCards
{
    public static IQueryable<Employee> WithAttendance(IQueryable<Employee> employees,IQueryable<AttendanceLog> logs,DateTime start,DateTime end) =>
        employees.Where(employee=>logs.Any(log=>log.EmployeeId==employee.Id && log.PunchTime>=start && log.PunchTime<end
            && ((log.BiometricDevice!=null && log.BiometricDevice.CommunicationMode==AttendanceRules.FieldCommunicationMode)||log.VerificationMode==AttendanceRules.FieldVerificationMode)));
    public static SiteEmployeeLocationItem Build(SiteEmployeeLocationItem profile,IEnumerable<AttendanceLog> logs)
    {
        var punches=logs.Where(x=>x.EmployeeId==profile.EmployeeId).OrderBy(x=>x.PunchTime).ThenBy(x=>x.Id).ToList();
        var checkIn=punches.FirstOrDefault(x=>AttendanceRules.NormalizePunchAction(x.PunchState)=="Check In");
        var checkOut=punches.LastOrDefault(x=>AttendanceRules.NormalizePunchAction(x.PunchState)=="Check Out");var last=punches.LastOrDefault();
        return profile with {
            CheckInSiteName=checkIn?.FieldSiteName,CheckOutSiteName=checkOut?.FieldSiteName,LastSiteName=last?.FieldSiteName,
            CheckInTime=checkIn?.PunchTime,CheckInLatitude=checkIn?.Latitude,CheckInLongitude=checkIn?.Longitude,CheckInAccuracyMetres=checkIn?.AccuracyMetres,CheckInSelfiePath=checkIn?.SelfiePath,
            CheckOutTime=checkOut?.PunchTime,CheckOutLatitude=checkOut?.Latitude,CheckOutLongitude=checkOut?.Longitude,CheckOutAccuracyMetres=checkOut?.AccuracyMetres,CheckOutSelfiePath=checkOut?.SelfiePath,
            LastLocationTime=last?.PunchTime,LastLatitude=last?.Latitude,LastLongitude=last?.Longitude
        };
    }
}

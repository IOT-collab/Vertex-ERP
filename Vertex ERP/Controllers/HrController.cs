using Microsoft.AspNetCore.Mvc;

namespace Vertex_ERP.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,HR")]
    public class HrController : Controller
    {
        public IActionResult EmployeeDashboard()
        {
            return View();
        }

        public IActionResult AttendanceDashboard()
        {
            return View();
        }

        public IActionResult EmpLeaveManagement()
        {
            return View();
        }

        public IActionResult EmpPayroll()
        {
            return View();
        }

        public IActionResult EmpPerformance()
        {
            return View();
        }

        public IActionResult Recuirement()
        {
            return View();
        }

        public IActionResult EmpDocuments()
            {
                return View();
        }

        public IActionResult Holiday()
        {
            return View();
        }
        public IActionResult Department()
        {
            return View();
        }

        public IActionResult ExpenseClaim()
        {
            return View();
        }

        public IActionResult AssetManagement()
        {
            return View();
        }

        public IActionResult Meetings()
        {
            return View();
        }

        public IActionResult HrmReports()
        {
            return View();
        }

    }
}

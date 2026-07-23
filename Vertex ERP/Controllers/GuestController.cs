using Microsoft.AspNetCore.Mvc;

namespace Shiva_Gautam.Controllers
{
    public class GuestController : Controller
    {
        public IActionResult Login()
        {
            return View();
        }
        [HttpPost]
        public IActionResult Login(string email, string password)
        {

            if (email == "abc@gmail.com" && password == "admin@123")
            {
                HttpContext.Session.SetString("email", email);
                return RedirectToAction("Dashboard");

            }
            ViewBag.ErrorMessage = "Invalid email or password";
            return View();
        }
        public IActionResult Dashboard()
        {
            return View();
        }

        public IActionResult Employees()
        {
            return View();
        }
        public IActionResult EmpAddRequirement()
        {
            return View();
        }

        public IActionResult Attendence()
        {
            return View();
        }

        public IActionResult AddAttendance()
        {
            return View();
        }

        public IActionResult Reports()
        {
            return View();
        }
        public IActionResult Hrms()
        {
            return View();
        }
        public IActionResult AddEmpHrm()
        {
             return View();
        }

        public IActionResult ProjectMgm()
        {
            return View();
        }
        public IActionResult AddProjectMgm()
        {
            return View();
        }

        public IActionResult DocumentMgm()
        {
            return View();
        }
        public IActionResult AddDocMgmSave()
        {
            return View();
        }

        public IActionResult AdminPanel()
        {
            return View();
        }

        public IActionResult AddAdminPanel()
        {
            return View();
        }

        public IActionResult Settings()
        {
            return View();
        }

        public IActionResult DepartmentManagement()
        {
            return View();
        }

        public IActionResult LeaveManagement()
                    {
            return View();
        }   
    }
}

using Microsoft.AspNetCore.Mvc;

namespace Vertex_ERP.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Employee,Admin,HR")]
    public class EmployeeDetailsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

using Microsoft.AspNetCore.Mvc;

namespace Vertex_ERP.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,HR")]
    public class ProjectMgmController : Controller
    {

        public IActionResult ProjectCreation()
        {
            return View();
        }

        public IActionResult AssignTask()
        {
            return View();
        }

        public IActionResult ProjectTimeline()
        {
            return View();
        }

        public IActionResult ResourceAllocation()
        {
            return View();
        }

        public IActionResult Timesheet()
        {
            return View();
        }

        public IActionResult BudgetTracking()
        {
            return View();
        }

        public IActionResult RaisedEnquiry()
        {
            return View();
        }


    }
}

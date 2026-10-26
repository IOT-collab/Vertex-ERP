using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Vertex_ERP.Controllers;

[Authorize(Roles = "Employee,User,Manager,HR,Admin")]
public sealed class ThemeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Vertex_ERP.Controllers;

[Authorize(Roles = "Employee,User,Manager,HR,Admin")]
public sealed class LanguageController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Save(string language)
    {
        if (language is not ("en" or "hi")) return BadRequest();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        Response.Cookies.Append("vertex.language." + userId, language, new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax,
            IsEssential = true, MaxAge = TimeSpan.FromDays(365), Path = "/"
        });
        return RedirectToAction(nameof(Index));
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vertex_ERP.Controllers;

void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label); }
LanguageController Controller(string? id)
{
    var http = new DefaultHttpContext();
    http.Request.Scheme = "https";
    if (id is not null) http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));
    return new LanguageController { ControllerContext = new ControllerContext { HttpContext = http } };
}
foreach (var language in new[] { "hi", "en" })
{
    var controller = Controller("17");
    Check(controller.Save(language) is RedirectToActionResult { ActionName: "Index" }, "Language save redirects to preferences");
    var cookie = controller.Response.Headers.SetCookie.ToString();
    Check(cookie.Contains("vertex.language.17=" + language), "Preference is scoped to authenticated account");
    Check(cookie.Contains("httponly") && cookie.Contains("secure") && cookie.Contains("samesite=lax"), "Cookie security flags");
}
Check(Controller("18").Save("fr") is BadRequestResult, "Only Hindi and English accepted");
Check(Controller(null).Save("hi") is UnauthorizedResult, "Missing identity cannot save a preference");
var second = Controller("18"); second.Save("hi");
Check(!second.Response.Headers.SetCookie.ToString().Contains("vertex.language.17"), "Saving one account does not overwrite another");
var auth = (AuthorizeAttribute)Attribute.GetCustomAttribute(typeof(LanguageController), typeof(AuthorizeAttribute))!;
Check(new[] { "Employee", "User", "Manager", "HR", "Admin" }.All(role => auth.Roles!.Split(',').Contains(role)), "Every login role has access");
Check(Attribute.IsDefined(typeof(LanguageController).GetMethod("Save")!, typeof(ValidateAntiForgeryTokenAttribute)), "Saving language requires CSRF validation");

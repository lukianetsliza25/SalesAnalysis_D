// SalesAnalysis.Web/Controllers/AccountController.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SalesAnalysis.Web.Models;
using SalesAnalysis.Data; // Додайте цей namespace
using System.Linq;

namespace SalesAnalysis.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<IdentityUser<int>> _userManager;
        private readonly SignInManager<IdentityUser<int>> _signInManager;
        private readonly SalesDbContext _context; // Додаємо контекст

        public AccountController(
            UserManager<IdentityUser<int>> userManager,
            SignInManager<IdentityUser<int>> signInManager,
            SalesDbContext context) // Додаємо в ін'єкцію
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
        }

        [HttpGet]
        public IActionResult Auth(string returnUrl = null)
        {
            // Якщо користувач вже в системі, робимо перенаправлення за логікою даних
            if (User.Identity.IsAuthenticated)
            {
                return RedirectBasedOnData();
            }
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // 1. Перевірка, чи існує користувач
                var existingUser = await _userManager.FindByEmailAsync(model.Email);
                if (existingUser != null)
                {
                    ModelState.AddModelError("", "Користувач з такою поштою вже зареєстрований.");
                    ViewData["ActiveTab"] = "register";
                    return View("Auth");
                }

                // 2. Створення користувача
                var user = new IdentityUser<int> { UserName = model.Email, Email = model.Email };
                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    // 3. Зберігаємо ім'я як Claim, щоб показувати його в навбарі
                    await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("FirstName", model.FirstName));

                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return RedirectBasedOnData();
                }

                foreach (var error in result.Errors)
                {
                    // Обробка помилок Identity (наприклад, занадто простий пароль)
                    ModelState.AddModelError("", error.Description);
                }
            }

            ViewData["ActiveTab"] = "register";
            return View("Auth");
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, false, false);
                if (result.Succeeded)
                {
                    return RedirectBasedOnData();
                }
                ModelState.AddModelError("", "Невірний логін або пароль");
            }
            ViewData["ActiveTab"] = "login";
            return View("Auth");
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
            return RedirectToAction("Auth", "Account");
        }

        // Допоміжний метод для перевірки даних
        private IActionResult RedirectBasedOnData()
        {
            // Отримуємо ID поточного користувача
            var userIdString = _userManager.GetUserId(User);

            // Якщо ID не знайдено (користувач не залогінився), йдемо на Auth
            if (string.IsNullOrEmpty(userIdString)) return RedirectToAction("Auth");

            int userId = int.Parse(userIdString);

            // ПЕРЕВІРКА: чи є дані саме у ЦЬОГО користувача
            bool hasData = _context.Transactions.Any(t => t.UserId == userId);

            if (hasData)
                return RedirectToAction("Index", "Dashboard");
            else
                return RedirectToAction("Index", "Import");
        }
    }
}
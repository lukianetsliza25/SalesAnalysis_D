// SalesAnalysis.Web/Controllers/ImportController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesAnalysis.Core.Entities;
using SalesAnalysis.Data.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

[Authorize]
public class ImportController : Controller
{
    private readonly ImportService _importService;

    public ImportController(ImportService importService)
    {
        _importService = importService;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            ViewBag.Message = "Помилка: Файл не обрано.";
            return View("Index");
        }

        try
        {
            // БЕЗПЕЧНЕ ОТРИМАННЯ ID КОРИСТУВАЧА (через стандартний NameIdentifier або Claim)
            var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdString))
            {
                ViewBag.Message = "Помилка: Користувач не авторизований.";
                return View("Index");
            }

            int userId = int.Parse(userIdString);

            await _importService.ClearPreviousDataAsync(userId);
            int importedCount;
            using (var stream = file.OpenReadStream())
            {
                importedCount = await _importService.ImportTransactionsFromCsvAsync(stream, userId);
            }

            if (importedCount > 0)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            else
            {
                ViewBag.Message = "Попередження: Імпортовано 0 транзакцій.";
            }
        }
        catch (Exception ex)
        {
            ViewBag.Message = $"Помилка імпорту: {ex.Message}";
        }

        return View("Index");
    }
}
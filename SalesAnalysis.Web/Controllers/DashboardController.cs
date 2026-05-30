// SalesAnalysis.Web/Controllers/DashboardController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SalesAnalysis.Core.Models;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

[Authorize]
public class DashboardController : Controller
{
    private readonly AnalysisService _analysisService;
    private readonly ClusteringService _clusteringService;
    private readonly PredictionService _predictionService;
    private readonly UserManager<IdentityUser<int>> _userManager;

    private const int PAGE_SIZE = 30;
    private const int PREDICTION_PERIODS = 12;

    public DashboardController(
        AnalysisService analysisService,
        ClusteringService clusteringService,
        PredictionService predictionService,
        UserManager<IdentityUser<int>> userManager)
    {
        _analysisService = analysisService;
        _clusteringService = clusteringService;
        _predictionService = predictionService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        // 1. Отримуємо ID поточного користувача
        var userStrId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userStrId)) return Challenge();
        int userId = int.Parse(userStrId);

        // 2. Перевірка наявності даних
        var totalTransactions = await _analysisService.GetTotalTransactionsAsync(userId);
        if (totalTransactions == 0)
        {
            return RedirectToAction("Index", "Import");
        }

        // --- 3. KPI ---
        ViewBag.TotalRevenue = await _analysisService.GetTotalRevenueAsync(userId);
        ViewBag.TotalTransactions = totalTransactions;

        var allData = await _analysisService.GetCustomerClusteringDataAsync(userId);
        int uniqueCustomers = allData.Count;

        ViewBag.UniqueCustomers = uniqueCustomers;
        ViewBag.AverageOrderValue = Math.Round(totalTransactions > 0 ? (decimal)ViewBag.TotalRevenue / totalTransactions : 0, 2);
        ViewBag.AvgCustomerSpend = Math.Round(uniqueCustomers > 0 ? (decimal)ViewBag.TotalRevenue / uniqueCustomers : 0, 2);
        ViewBag.AvgFrequency = Math.Round(uniqueCustomers > 0 ? (float)totalTransactions / uniqueCustomers : 0, 2);

        var monthlyData = await _analysisService.GetMonthlySalesDataAsync(userId);
        var monthlyKpi = await _analysisService.GetMonthlyKpiDataAsync(userId);
        ViewBag.KpiHistory = monthlyKpi;

        if (monthlyData.Any())
        {
            var best = monthlyData.OrderByDescending(m => m.SalesAmount).First();
            var worst = monthlyData.OrderBy(m => m.SalesAmount).First();
            ViewBag.BestMonth = best.SalesAmount;
            ViewBag.BestMonthName = $"Місяць #{best.TimeIndex}";
            ViewBag.WorstMonth = worst.SalesAmount;
            ViewBag.WorstMonthName = $"Місяць #{worst.TimeIndex}";
        }

        // --- 4. КЛАСТЕРИЗАЦІЯ (Виправлено логіку) ---
        var result = new List<ClusteredCustomer>();
        if (allData.Any())
        {
            var spentValues = allData.Select(x => x.TotalSpent).Where(x => x > 0).OrderBy(x => x).ToList();
            var freqValues = allData.Select(x => x.PurchaseFrequency).OrderBy(x => x).ToList();

            float GetPercentile(List<float> list, double p)
            {
                if (!list.Any()) return 0;
                int idx = (int)((list.Count - 1) * p);
                return list[idx];
            }

            float p99Spent = GetPercentile(spentValues, 0.99);
            float p99Freq = GetPercentile(freqValues, 0.99);

            var normal = allData.Where(c => c.TotalSpent > 0 && c.TotalSpent <= p99Spent && c.PurchaseFrequency <= p99Freq).ToList();
            var anomalies = allData.Except(normal).ToList();

            if (normal.Any())
            {
                var clusterModel = _clusteringService.TrainAndSaveModel(_clusteringService.MLContext.Data.LoadFromEnumerable(normal));
                var predictions = normal.Select(c => new { Data = c, Pred = _clusteringService.Predict(clusterModel, c) }).ToList();

                // Обчислюємо центри кластерів для мапінгу
                var stats = predictions.GroupBy(p => p.Pred.PredictedClusterId)
                    .Select(g => new { Id = g.Key, AvgSpent = g.Average(x => x.Data.TotalSpent) }).OrderByDescending(x => x.AvgSpent).ToList();

                foreach (var item in predictions)
                {
                    // Логіка: найвищий дохід - VIP (3), найнижчий - Новий (1), інше - Середній (2)
                    int logicalId = 2;
                    if (stats.Count > 0 && item.Pred.PredictedClusterId == stats[0].Id) logicalId = 3;
                    else if (stats.Count > 1 && item.Pred.PredictedClusterId == stats.Last().Id) logicalId = 1;

                    result.Add(new ClusteredCustomer
                    {
                        CustomerId = item.Data.CustomerId,
                        TotalSpent = item.Data.TotalSpent,
                        PurchaseFrequency = item.Data.PurchaseFrequency,
                        ClusterId = logicalId,
                        ClusterDescription = logicalId == 3 ? "Високоцінний (VIP)" : (logicalId == 1 ? "Новий/Рідкісний" : "Середній")
                    });
                }
            }
            foreach (var a in anomalies)
            {
                result.Add(new ClusteredCustomer { CustomerId = a.CustomerId, TotalSpent = a.TotalSpent, PurchaseFrequency = a.PurchaseFrequency, ClusterId = 0, ClusterDescription = "Аномалія" });
            }
        }

        // --- 5. ПАГІНАЦІЯ ---
        int total = result.Count;
        int totalPages = (int)Math.Ceiling(total / (double)PAGE_SIZE);
        int currentPage = 1;
        if (Request.Query.ContainsKey("page") && int.TryParse(Request.Query["page"], out int pVal))
            currentPage = Math.Clamp(pVal, 1, totalPages > 0 ? totalPages : 1);

        ViewBag.ClusteredCustomers = result.OrderBy(c => c.CustomerId).Skip((currentPage - 1) * PAGE_SIZE).Take(PAGE_SIZE).ToList();
        ViewBag.TotalCustomers = total;
        ViewBag.CurrentPage = currentPage;
        ViewBag.TotalPages = totalPages;

        // --- 6. ПРОГНОЗУВАННЯ (З кешуванням) ---
        // --- 6. ПРОГНОЗУВАННЯ (З кешуванням та виправленням аномалій) ---
        // --- 6. ПРОГНОЗУВАННЯ ---
        // --- 6. ПРОГНОЗУВАННЯ (Оновлено: без фільтрації аномалій + надійний кеш) ---
        List<float> historyData = monthlyData.Select(d => d.SalesAmount).ToList();
        List<float> predictionData = new List<float>();

        // Спробуємо дістати вже існуючий прогноз з бази
        var cachedJson = await _analysisService.GetLastAnalysisResultAsync(userId, "SalesForecast");

        if (!string.IsNullOrEmpty(cachedJson) && cachedJson != "[]")
        {
            // Якщо в базі є дані — використовуємо їх
            predictionData = JsonSerializer.Deserialize<List<float>>(cachedJson);
            ViewBag.NextMonthPrediction = predictionData?.FirstOrDefault() ?? 0.0f;
        }
        else if (monthlyData.Count >= 2) // Достатньо хоча б 2-х місяців для спроби навчання
        {
            try
            {
                // Навчаємо модель на ВСІХ наявних місячних даних без фільтрації
                var predictionModel = _predictionService.TrainAndSaveModel(
                    _predictionService.MLContext.Data.LoadFromEnumerable(monthlyData));

                // Визначаємо параметри для старту прогнозу
                var lastMonthEntry = monthlyData.OrderByDescending(d => d.TimeIndex).First();
                var nextIndex = lastMonthEntry.TimeIndex + 1;
                int lastMonthValue = (int)lastMonthEntry.MonthOfYear;

                // Робимо прогноз на 12 місяців
                predictionData = _predictionService.PredictNPeriods(
                    predictionModel,
                    nextIndex,
                    PREDICTION_PERIODS,
                    lastMonthValue);

                if (predictionData != null && predictionData.Any())
                {
                    ViewBag.NextMonthPrediction = predictionData.FirstOrDefault();

                    // Зберігаємо в базу, щоб наступного разу не обчислювати заново
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "SalesForecast", predictionData);
                }
            }
            catch (Exception ex)
            {
                // Це запише помилку у вікно Output у Visual Studio
                System.Diagnostics.Debug.WriteLine("---------- ML ERROR ----------");
                System.Diagnostics.Debug.WriteLine(ex.Message);
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);

                // Це виведе помилку прямо на плашку прогнозу в інтерфейсі (для тесту)
                ViewBag.NextMonthPredictionError = ex.Message;
                ViewBag.NextMonthPrediction = 0.0f;
            }
        }

        // Передача даних у View для графіка
        ViewBag.HistoryDataJson = JsonSerializer.Serialize(historyData);
        ViewBag.PredictionDataJson = JsonSerializer.Serialize(predictionData);
        return View();
    }
}
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

        // 2. Швидка перевірка: чи взагалі є якісь дані у користувача (щоб не пускати далі, якщо база порожня)
        var totalTransactions = await _analysisService.GetTotalTransactionsAsync(userId);
        if (totalTransactions == 0)
        {
            return RedirectToAction("Index", "Import");
        }

        // Ініціалізуємо змінні для збереження аналітичних зрізів
        var result = new List<ClusteredCustomer>();
        var monthlyKpi = new List<MonthlyKpiData>();
        var monthlyData = new List<SalesDataPoint>();
        List<float> historyData = new List<float>();
        List<float> predictionData = new List<float>();

        // Спробуємо дістати вже існуючі збережені результати з таблиці `SavedAnalyses`
        var cachedClustersJson = await _analysisService.GetLastAnalysisResultAsync(userId, "CustomerClusters");
        var cachedKpiJson = await _analysisService.GetLastAnalysisResultAsync(userId, "MonthlyKpiHistory");
        var cachedForecastJson = await _analysisService.GetLastAnalysisResultAsync(userId, "SalesForecast");

        // Ознака того, чи вдалося нам відновити базову аналітику з бази даних
        bool isClusteringCached = !string.IsNullOrEmpty(cachedClustersJson) && cachedClustersJson != "[]";
        bool isKpiCached = !string.IsNullOrEmpty(cachedKpiJson) && cachedKpiJson != "[]";
        bool isForecastCached = !string.IsNullOrEmpty(cachedForecastJson) && cachedForecastJson != "[]";

        // =========================================================================
        // СЦЕНАРІЙ А: ВСІ ДАНІ ЗНАЙДЕНО В КЕШІ (Сторінка завантажується за мікросекунди)
        // =========================================================================
        if (isClusteringCached && isKpiCached)
        {
            // 1. Відновлюємо сегментацію клієнтів
            result = JsonSerializer.Deserialize<List<ClusteredCustomer>>(cachedClustersJson) ?? new List<ClusteredCustomer>();

            // 2. Відновлюємо історію місячних KPI
            monthlyKpi = JsonSerializer.Deserialize<List<MonthlyKpiData>>(cachedKpiJson) ?? new List<MonthlyKpiData>();
            ViewBag.KpiHistory = monthlyKpi;

            // 3. Формуємо базові KPI картки на основі збереженого масиву
            ViewBag.TotalRevenue = await _analysisService.GetTotalRevenueAsync(userId);
            ViewBag.TotalTransactions = totalTransactions;
            ViewBag.UniqueCustomers = result.Count;
            ViewBag.AverageOrderValue = Math.Round(totalTransactions > 0 ? (decimal)ViewBag.TotalRevenue / totalTransactions : 0, 2);
            ViewBag.AvgCustomerSpend = Math.Round(result.Count > 0 ? (decimal)ViewBag.TotalRevenue / result.Count : 0, 2);
            ViewBag.AvgFrequency = Math.Round(result.Count > 0 ? (float)totalTransactions / result.Count : 0, 2);

            // 4. Отримуємо чисті історичні точки продажів для графіка (вони потрібні завжди для рендерингу)
            monthlyData = await _analysisService.GetMonthlySalesDataAsync(userId);
            historyData = monthlyData.Select(d => d.SalesAmount).ToList();

            if (monthlyData.Any())
            {
                var best = monthlyData.OrderByDescending(m => m.SalesAmount).First();
                var worst = monthlyData.OrderBy(m => m.SalesAmount).First();
                ViewBag.BestMonth = best.SalesAmount;
                ViewBag.BestMonthName = $"Місяць #{best.TimeIndex}";
                ViewBag.WorstMonth = worst.SalesAmount;
                ViewBag.WorstMonthName = $"Місяць #{worst.TimeIndex}";
            }

            // 5. Відновлюємо прогноз часового ряду
            if (isForecastCached)
            {
                predictionData = JsonSerializer.Deserialize<List<float>>(cachedForecastJson) ?? new List<float>();
                ViewBag.NextMonthPrediction = predictionData.FirstOrDefault();
            }
        }
        // =========================================================================
        // СЦЕНАРІЙ Б: КЕШУ НЕМАЄ (Перший запуск після імпорту файлу)
        // =========================================================================
        else
        {
            // 1. Розраховуємо загальні KPI з бази даних в реальному часі
            ViewBag.TotalRevenue = await _analysisService.GetTotalRevenueAsync(userId);
            ViewBag.TotalTransactions = totalTransactions;

            var allData = await _analysisService.GetCustomerClusteringDataAsync(userId);
            int uniqueCustomers = allData.Count;

            ViewBag.UniqueCustomers = uniqueCustomers;
            ViewBag.AverageOrderValue = Math.Round(totalTransactions > 0 ? (decimal)ViewBag.TotalRevenue / totalTransactions : 0, 2);
            ViewBag.AvgCustomerSpend = Math.Round(uniqueCustomers > 0 ? (decimal)ViewBag.TotalRevenue / uniqueCustomers : 0, 2);
            ViewBag.AvgFrequency = Math.Round(uniqueCustomers > 0 ? (float)totalTransactions / uniqueCustomers : 0, 2);

            monthlyData = await _analysisService.GetMonthlySalesDataAsync(userId);
            monthlyKpi = await _analysisService.GetMonthlyKpiDataAsync(userId);
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

            // 2. НАВЧАННЯ МОДЕЛІ КЛАСТЕРИЗАЦІЇ K-MEANS
            if (allData.Any())
            {
                try
                {
                    var clusteringDataView = _clusteringService.MLContext.Data.LoadFromEnumerable(allData);
                    var clusterModel = _clusteringService.TrainAndSaveModel(clusteringDataView);
                    var predictions = allData.Select(c => new { Data = c, Pred = _clusteringService.Predict(clusterModel, c) }).ToList();

                    var clusterProfiles = predictions
                        .GroupBy(p => p.Pred.PredictedClusterId)
                        .Select(g => new { ClusterId = g.Key, AvgSpent = g.Average(x => x.Data.TotalSpent) })
                        .OrderByDescending(x => x.AvgSpent).ToList();

                    uint vipClusterId = clusterProfiles.FirstOrDefault()?.ClusterId ?? 0;
                    uint lowClusterId = clusterProfiles.LastOrDefault()?.ClusterId ?? 0;

                    foreach (var item in predictions)
                    {
                        int logicalId = (item.Pred.PredictedClusterId == vipClusterId) ? 3 : ((item.Pred.PredictedClusterId == lowClusterId) ? 1 : 2);
                        string description = (logicalId == 3) ? "Високоцінний (VIP)" : ((logicalId == 1) ? "Новий / Рідкісний" : "Постійний (Середній)");

                        result.Add(new ClusteredCustomer
                        {
                            CustomerId = item.Data.CustomerId,
                            TotalSpent = item.Data.TotalSpent,
                            PurchaseFrequency = item.Data.PurchaseFrequency,
                            ClusterId = logicalId,
                            ClusterDescription = description
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clustering Error: {ex.Message}");
                    foreach (var c in allData)
                    {
                        result.Add(new ClusteredCustomer { CustomerId = c.CustomerId, TotalSpent = c.TotalSpent, PurchaseFrequency = c.PurchaseFrequency, ClusterId = 2, ClusterDescription = "Постійний (Середній)" });
                    }
                }
            }

            // 3. НАВЧАННЯ МОДЕЛІ ПРОГНОЗУВАННЯ
            historyData = monthlyData.Select(d => d.SalesAmount).ToList();

            if (isForecastCached)
            {
                predictionData = JsonSerializer.Deserialize<List<float>>(cachedForecastJson) ?? new List<float>();
                ViewBag.NextMonthPrediction = predictionData.FirstOrDefault();
            }
            else if (monthlyData.Count >= 2)
            {
                try
                {
                    var predictionModel = _predictionService.TrainAndSaveModel(_predictionService.MLContext.Data.LoadFromEnumerable(monthlyData));
                    var lastMonthEntry = monthlyData.OrderByDescending(d => d.TimeIndex).First();

                    predictionData = _predictionService.PredictNPeriods(predictionModel, lastMonthEntry.TimeIndex + 1, PREDICTION_PERIODS, (int)lastMonthEntry.MonthOfYear);

                    if (predictionData != null && predictionData.Any())
                    {
                        ViewBag.NextMonthPrediction = predictionData.FirstOrDefault();
                        await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "SalesForecast", predictionData);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ML Forecast Error: {ex.Message}");
                    ViewBag.NextMonthPrediction = 0.0f;
                }
            }

            // 4. ГАРАНТОВАНЕ ЗБЕРЕЖЕННЯ РЕЗУЛЬТАТІВ У КЕШ ТАБЛИЦІ
            try
            {
                if (monthlyKpi != null && monthlyKpi.Any())
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "MonthlyKpiHistory", monthlyKpi);

                if (result != null && result.Any())
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "CustomerClusters", result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving analysis to DB: {ex.Message}");
            }
        }

        // --- 5. ПАГІНАЦІЯ СЕГМЕНТОВАНИХ КЛІЄНТІВ ---
        int total = result.Count;
        int totalPages = (int)Math.Ceiling(total / (double)PAGE_SIZE);
        int currentPage = 1;
        if (Request.Query.ContainsKey("page") && int.TryParse(Request.Query["page"], out int pVal))
            currentPage = Math.Clamp(pVal, 1, totalPages > 0 ? totalPages : 1);

        ViewBag.ClusteredCustomers = result.OrderBy(c => c.CustomerId).Skip((currentPage - 1) * PAGE_SIZE).Take(PAGE_SIZE).ToList();
        ViewBag.TotalCustomers = total;
        ViewBag.CurrentPage = currentPage;
        ViewBag.TotalPages = totalPages;

        // Передача серіалізованих масивів у JavaScript представлення
        ViewBag.HistoryDataJson = JsonSerializer.Serialize(historyData);
        ViewBag.PredictionDataJson = JsonSerializer.Serialize(predictionData);
        return View();
    }
}
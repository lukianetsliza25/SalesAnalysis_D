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

        var result = new List<ClusteredCustomer>();
        var monthlyKpi = new List<MonthlyKpiData>();
        var monthlyData = new List<SalesDataPoint>();
        List<float> historyData = new List<float>();

        List<float> predictionDataFastTree = new List<float>();
        List<float> predictionDataLinear = new List<float>();

        // Спробуємо дістати вже існуючі збережені результати з бази
        var cachedClustersJson = await _analysisService.GetLastAnalysisResultAsync(userId, "CustomerClusters");
        var cachedKpiJson = await _analysisService.GetLastAnalysisResultAsync(userId, "MonthlyKpiHistory");

        var cachedForecastFastTree = await _analysisService.GetLastAnalysisResultAsync(userId, "SalesForecastFastTree");
        var cachedForecastLinear = await _analysisService.GetLastAnalysisResultAsync(userId, "SalesForecastLinear");
        var cachedBestModelName = await _analysisService.GetLastAnalysisResultAsync(userId, "BestModelName");

        // КЛЮЧОВЕ ОНОВЛЕННЯ: дістаємо збережені похибки та точність з бази
        var cachedR2FastTree = await _analysisService.GetLastAnalysisResultAsync(userId, "R2FastTree");
        var cachedR2Linear = await _analysisService.GetLastAnalysisResultAsync(userId, "R2Linear");
        var cachedRmseFastTree = await _analysisService.GetLastAnalysisResultAsync(userId, "RmseFastTree");
        var cachedRmseLinear = await _analysisService.GetLastAnalysisResultAsync(userId, "RmseLinear");

        bool isClusteringCached = !string.IsNullOrEmpty(cachedClustersJson) && cachedClustersJson != "[]";
        bool isKpiCached = !string.IsNullOrEmpty(cachedKpiJson) && cachedKpiJson != "[]";

        // =========================================================================
        // СЦЕНАРІЙ А: ВСІ ДАНІ ЗНАЙДЕНО В КЕШІ (Швидке завантаження без ML-навантаження)
        // =========================================================================
        if (isClusteringCached && isKpiCached)
        {
            result = JsonSerializer.Deserialize<List<ClusteredCustomer>>(cachedClustersJson) ?? new List<ClusteredCustomer>();
            monthlyKpi = JsonSerializer.Deserialize<List<MonthlyKpiData>>(cachedKpiJson) ?? new List<MonthlyKpiData>();
            ViewBag.KpiHistory = monthlyKpi;

            ViewBag.TotalRevenue = await _analysisService.GetTotalRevenueAsync(userId);
            ViewBag.TotalTransactions = totalTransactions;
            ViewBag.UniqueCustomers = result.Count;
            ViewBag.AverageOrderValue = Math.Round(totalTransactions > 0 ? (decimal)ViewBag.TotalRevenue / totalTransactions : 0, 2);
            ViewBag.AvgCustomerSpend = Math.Round(result.Count > 0 ? (decimal)ViewBag.TotalRevenue / result.Count : 0, 2);
            ViewBag.AvgFrequency = Math.Round(result.Count > 0 ? (float)totalTransactions / result.Count : 0, 2);

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

            predictionDataFastTree = !string.IsNullOrEmpty(cachedForecastFastTree) ? JsonSerializer.Deserialize<List<float>>(cachedForecastFastTree) : new List<float>();
            predictionDataLinear = !string.IsNullOrEmpty(cachedForecastLinear) ? JsonSerializer.Deserialize<List<float>>(cachedForecastLinear) : new List<float>();

            ViewBag.BestModelText = !string.IsNullOrEmpty(cachedBestModelName) ? JsonSerializer.Deserialize<string>(cachedBestModelName) : "Не визначено";

            // Відновлюємо метрики точності з кешу для панелі розробника
            ViewBag.R2FastTree = !string.IsNullOrEmpty(cachedR2FastTree) ? JsonSerializer.Deserialize<double>(cachedR2FastTree) : 0.0;
            ViewBag.R2Linear = !string.IsNullOrEmpty(cachedR2Linear) ? JsonSerializer.Deserialize<double>(cachedR2Linear) : 0.0;
            ViewBag.RmseFastTree = !string.IsNullOrEmpty(cachedRmseFastTree) ? JsonSerializer.Deserialize<int>(cachedRmseFastTree) : 0;
            ViewBag.RmseLinear = !string.IsNullOrEmpty(cachedRmseLinear) ? JsonSerializer.Deserialize<int>(cachedRmseLinear) : 0;
        }
        // =========================================================================
        // СЦЕНАРІЙ Б: КЕШУ НЕМАЄ (Перший запуск моделі, тренування та аналіз похибок)
        // =========================================================================
        else
        {
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

            // Кластеризація K-Means
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

                        result.Add(new ClusteredCustomer { CustomerId = item.Data.CustomerId, TotalSpent = item.Data.TotalSpent, PurchaseFrequency = item.Data.PurchaseFrequency, ClusterId = logicalId, ClusterDescription = description });
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

            // --- 6. ПОРІВНЯННЯ МОДЕЛЕЙ ПРОГНОЗУВАННЯ ---
            historyData = monthlyData.Select(d => d.SalesAmount).ToList();
            string bestModelText = "Недостатньо даних";

            if (monthlyData.Count >= 2)
            {
                try
                {
                    var dataView = _predictionService.MLContext.Data.LoadFromEnumerable(monthlyData);
                    var lastMonthEntry = monthlyData.OrderByDescending(d => d.TimeIndex).First();
                    float nextIndex = lastMonthEntry.TimeIndex + 1;
                    int lastMonthValue = (int)lastMonthEntry.MonthOfYear;

                    var modelFastTree = _predictionService.TrainAndSaveModel(dataView);
                    var modelLinear = _predictionService.TrainAndSaveLinearModel(dataView);

                    predictionDataFastTree = _predictionService.PredictNPeriods(modelFastTree, nextIndex, PREDICTION_PERIODS, lastMonthValue);
                    predictionDataLinear = _predictionService.PredictNPeriods(modelLinear, nextIndex, PREDICTION_PERIODS, lastMonthValue);

                    // Математична оцінка точності
                    var evaluateFastTree = _predictionService.MLContext.Regression.Evaluate(modelFastTree.Transform(dataView), "Label");
                    var evaluateLinear = _predictionService.MLContext.Regression.Evaluate(modelLinear.Transform(dataView), "Label");

                    double r2FastTree = Math.Round(Math.Max(0, evaluateFastTree.RSquared), 2);
                    double r2Linear = Math.Round(Math.Max(0, evaluateLinear.RSquared), 2);

                    // Отримуємо чисту похибку у вигляді цілого числа (гривень)
                    int rmseFastTree = (int)evaluateFastTree.RootMeanSquaredError;
                    int rmseLinear = (int)evaluateLinear.RootMeanSquaredError;

                    // Записуємо у ViewBag
                    ViewBag.R2FastTree = r2FastTree;
                    ViewBag.R2Linear = r2Linear;
                    ViewBag.RmseFastTree = rmseFastTree;
                    ViewBag.RmseLinear = rmseLinear;

                    if (r2FastTree >= r2Linear)
                    {
                        bestModelText = "FastTree (Дерева рішень)";
                    }
                    else
                    {
                        bestModelText = "Лінійна Регресія (SDCA)";
                    }

                    ViewBag.BestModelText = bestModelText;

                    // Зберігаємо прогнози та похибки в базу даних
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "SalesForecastFastTree", predictionDataFastTree);
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "SalesForecastLinear", predictionDataLinear);
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "BestModelName", bestModelText);

                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "R2FastTree", r2FastTree);
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "R2Linear", r2Linear);
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "RmseFastTree", rmseFastTree);
                    await _analysisService.SaveAnalysisResultAsync(userId, "ALL", "RmseLinear", rmseLinear);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ML Forecast Error: {ex.Message}");
                    bestModelText = $"Помилка ML: {ex.Message}";
                    ViewBag.BestModelText = bestModelText;
                }
            }

            // Зберігаємо KPI та кластери
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

        // Пагінація
        int total = result.Count;
        int totalPages = (int)Math.Ceiling(total / (double)PAGE_SIZE);
        int currentPage = 1;
        if (Request.Query.ContainsKey("page") && int.TryParse(Request.Query["page"], out int pVal))
            currentPage = Math.Clamp(pVal, 1, totalPages > 0 ? totalPages : 1);

        ViewBag.ClusteredCustomers = result.OrderBy(c => c.CustomerId).Skip((currentPage - 1) * PAGE_SIZE).Take(PAGE_SIZE).ToList();
        ViewBag.TotalCustomers = total;
        ViewBag.CurrentPage = currentPage;
        ViewBag.TotalPages = totalPages;

        ViewBag.HistoryDataJson = JsonSerializer.Serialize(historyData);
        ViewBag.PredictionFastTreeJson = JsonSerializer.Serialize(predictionDataFastTree);
        ViewBag.PredictionLinearJson = JsonSerializer.Serialize(predictionDataLinear);

        ViewBag.KpiHistoryJson = JsonSerializer.Serialize(monthlyKpi);
        return View();
    }
}
//SalesAnalysis.Data/Services/AnalysisService.cs
using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Data;
using SalesAnalysis.Core.Models;
using SalesAnalysis.Core.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Text.Json;

namespace SalesAnalysis.Data.Services
{
    public class AnalysisService
    {
        private readonly SalesDbContext _context;

        // Використовуємо пряму ін'єкцію контексту для стабільної роботи з БД
        public AnalysisService(SalesDbContext context)
        {
            _context = context;
        }

        // 1. Метод обчислення загального доходу користувача
        public async Task<decimal> GetTotalRevenueAsync(int userId)
        {
            // Фільтрація транзакцій за UserId поточного користувача
            var query = _context.Transactions.Where(t => t.UserId == userId);

            // Перевірка наявності даних для запобігання помилкам
            if (!await query.AnyAsync()) return 0m;

            // Обчислення суми виконується на стороні СКБД PostgreSQL за допомогою SumAsync
            return await query.SumAsync(t => t.Revenue);
        }

        // 2. Метод отримання загальної кількості транзакцій користувача
        public async Task<int> GetTotalTransactionsAsync(int userId)
        {
            // Підрахунок кількості рядків у таблиці транзакцій для конкретного користувача
            return await _context.Transactions
                .Where(t => t.UserId == userId)
                .CountAsync();
        }

        // 3. Метод формування RFM-даних для кластеризації клієнтів
        public async Task<List<CustomerData>> GetCustomerClusteringDataAsync(int userId)
        {
            // Отримання списку всіх транзакцій користувача з бази даних
            var allTransactions = await _context.Transactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            if (!allTransactions.Any()) return new List<CustomerData>();

            // Динамічне визначення точки відліку для Recency (остання дата + 1 день)
            var latestDate = allTransactions.Max(t => t.Date);
            var today = latestDate.AddDays(1);

            // Групування даних по клієнтах та розрахунок ознак RFM
            return allTransactions
                .GroupBy(t => t.CustomerId)
                .Select(g => new CustomerData
                {
                    CustomerId = g.Key,
                    TotalSpent = (float)g.Sum(t => t.Revenue), // Monetary: сума витрат клієнта
                    PurchaseFrequency = g.Count(),             // Frequency: кількість покупок клієнта
                    DaysSinceLastPurchase = (float)(today - g.Max(t => t.Date)).TotalDays // Recency: давність останньої покупки
                })
                .ToList();
        }

        // 4. Метод агрегації продажів за місяцями (для графіка та ML)
        public async Task<List<SalesDataPoint>> GetMonthlySalesDataAsync(int userId)
        {
            // Витягування транзакцій для формування точок часового ряду
            var allTransactions = await _context.Transactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            if (!allTransactions.Any()) return new List<SalesDataPoint>();

            // 1. Знаходимо найсвіжішу дату транзакції в усьому датасеті
            var maxDate = allTransactions.Max(t => t.Date);

            // 2. Визначаємо останній можливий день для цього місяця (наприклад, для лютого — 28 або 29)
            int daysInMaxMonth = DateTime.DaysInMonth(maxDate.Year, maxDate.Month);

            // Групування даних по місяцях та хронологічне сортування
            var groupedMonths = allTransactions
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .ToList();

            var result = new List<SalesDataPoint>();
            int index = 1; // Порядковий номер періоду (TimeIndex для регресії)

            foreach (var g in groupedMonths)
            {
                // 3. ФІЛЬТРАЦІЯ: Перевірка логічних маркерів неповного календарного періоду
                bool isLastMonthInDataset = (g.Key.Year == maxDate.Year && g.Key.Month == maxDate.Month);
                bool isCurrentCalendarMonth = (g.Key.Year == DateTime.UtcNow.Year && g.Key.Month == DateTime.UtcNow.Month);

                // Якщо останній місяць вибірки не закритий (менше 25-26 днів), пропускаємо його задля точності прогнозу
                if (isLastMonthInDataset && (maxDate.Day < (daysInMaxMonth - 2) || isCurrentCalendarMonth))
                {
                    continue; // Пропускаємо цей неповний місяць
                }

                // Додавання сформованої точки часового ряду до фінального списку
                result.Add(new SalesDataPoint
                {
                    TimeIndex = index++,              // Часовий крок періоду (ознака лінійного тренду)
                    MonthOfYear = (float)g.Key.Month, // Календарний номер місяця (ознака сезонності)
                    SalesAmount = (float)g.Sum(t => t.Revenue) // Загальна сума виторгу (цільова мітка Label)
                });
            }

            return result;
        }

        // 5. Метод обчислення розширених місячних KPI
        public async Task<List<MonthlyKpiData>> GetMonthlyKpiDataAsync(int userId)
        {
            // Отримання масиву даних для розрахунку місячної статистики
            var all = await _context.Transactions
                .Where(t => t.UserId == userId)
                .ToListAsync();

            if (!all.Any()) return new List<MonthlyKpiData>();

            var maxDate = all.Max(t => t.Date);
            int daysInMaxMonth = DateTime.DaysInMonth(maxDate.Year, maxDate.Month);

            var grouped = all
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .ToList();

            var filteredGrouped = new List<MonthlyKpiData>();

            foreach (var g in grouped)
            {
                // Умова фільтрації неповного місяця, ідентична до графіка
                bool isLastMonthInDataset = (g.Key.Year == maxDate.Year && g.Key.Month == maxDate.Month);
                bool isCurrentCalendarMonth = (g.Key.Year == DateTime.UtcNow.Year && g.Key.Month == DateTime.UtcNow.Month);

                if (isLastMonthInDataset && (maxDate.Day < (daysInMaxMonth - 2) || isCurrentCalendarMonth))
                {
                    continue; // Пропускаємо цей місяць в історії KPI
                }

                // Збір базових показників за повний календарний місяць
                filteredGrouped.Add(new MonthlyKpiData
                {
                    MonthIndex = $"{g.Key.Year}-{g.Key.Month:D2}", // Рядковий індекс періоду (напр. "2024-01")
                    TotalRevenue = (float)g.Sum(x => x.Revenue),   // Сумарний виторг
                    TotalTransactions = g.Count(),                 // Кількість чеків
                    UniqueCustomers = g.Select(x => x.CustomerId).Distinct().Count() // Кількість унікальних покупців
                });
            }

            // Розрахунок похідних метрик для відфільтрованих повних місяців
            foreach (var m in filteredGrouped)
            {
                m.AverageOrderValue = m.TotalTransactions > 0 ? m.TotalRevenue / m.TotalTransactions : 0; // Середній чек
                m.CustomerSpend = m.UniqueCustomers > 0 ? m.TotalRevenue / m.UniqueCustomers : 0;        // Витрати на клієнта
                m.Frequency = m.UniqueCustomers > 0 ? (float)m.TotalTransactions / m.UniqueCustomers : 0; // Частота покупок
            }

            return filteredGrouped;
        }

        // 6. Метод збереження результатів аналізу (Прогнозів/Кластерів)
        public async Task SaveAnalysisResultAsync(int userId, string productId, string type, object result)
        {
            // Видаляємо попередній аналіз такого ж типу перед збереженням нового (очищення кешу)
            var existing = _context.SavedAnalyses
                .Where(a => a.UserId == userId && a.AnalysisType == type && a.ProductId == productId);

            _context.SavedAnalyses.RemoveRange(existing);

            // Підготовка нової сутності кешу аналітики для запису в БД
            var saved = new SavedAnalysis
            {
                UserId = userId,
                ProductId = productId,
                AnalysisType = type,
                ResultJson = JsonSerializer.Serialize(result), // Серіалізація об'єктів обчислень ML у JSON-рядок
                CreatedAt = DateTime.UtcNow // Час створення запису за стандартом UTC
            };

            await _context.SavedAnalyses.AddAsync(saved);
            await _context.SaveChangesAsync(); // Гарантоване збереження транзакції в БД
        }

        // 7. Метод отримання останнього збереженого результату
        public async Task<string> GetLastAnalysisResultAsync(int userId, string type)
        {
            // Пошук у таблиці кешу, сортування за часом та вибір найсвіжішого результату
            var analysis = await _context.SavedAnalyses
                .Where(a => a.UserId == userId && a.AnalysisType == type)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            return analysis?.ResultJson;
        }
    }
}
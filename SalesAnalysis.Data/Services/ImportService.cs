//SalesAnalysis.Data/Services/ImportService.cs
using CsvHelper.Configuration;
using CsvHelper;
using SalesAnalysis.Core.Entities;
using System.Globalization;
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CsvHelper.TypeConversion;
using System.Globalization;

namespace SalesAnalysis.Data.Services
{
    // Клас ImportService (повний код для контексту)
    public class ImportService
    {
        private readonly IServiceProvider _serviceProvider;

        public ImportService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<int> ImportTransactionsFromCsvAsync(Stream fileStream, int userId) // Додали userId
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
                var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };

                using var reader = new StreamReader(fileStream);
                using var csv = new CsvReader(reader, config);

                // Налаштування форматів дати...
                var options = csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>();
                options.Formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" };
                csv.Context.RegisterClassMap<TransactionMap>();

                try
                {
                    var transactions = csv.GetRecords<Transaction>().ToList();

                    // ПРИВ'ЯЗКА: кожній транзакції призначаємо власника
                    foreach (var t in transactions)
                    {
                        t.UserId = userId;
                    }

                    await context.Transactions.AddRangeAsync(transactions);
                    return await context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Помилка: {ex.Message}");
                }
            }
        }

        public async Task ClearPreviousDataAsync(int userId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

                // Швидке видалення прямо в БД (ExecuteDelete)
                await context.Transactions
                    .Where(t => t.UserId == userId)
                    .ExecuteDeleteAsync();

                await context.SavedAnalyses
                    .Where(a => a.UserId == userId)
                    .ExecuteDeleteAsync();

                // SaveChangesAsync тут вже не потрібен для ExecuteDelete, 
                // бо команда виконується миттєво
            }
        }
    }

    // -----------------------------------------------------
    // Конвертер для автоматичного розрахунку доходу (Revenue)
    // Revenue = Quantity × UnitPrice
    // Використовується під час імпорту CSV-файлу
    public class RevenueConverter : DefaultTypeConverter
    {
        // Метод викликається CsvHelper під час зчитування значення з CSV
        public override object ConvertFromString(
            string text,
            IReaderRow row,
            MemberMapData memberMapData)
        {
            // Отримання значень кількості та ціни за одиницю
            // безпосередньо з рядка CSV-файлу
            var quantityString = row.GetField<string>("Quantity");
            var unitPriceString = row.GetField<string>("UnitPrice");

            // Безпечний парсинг числових значень
            if (int.TryParse(quantityString, out int quantity) &&
                decimal.TryParse(
                    unitPriceString,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal unitPrice))
            {
                // Обчислення доходу як добутку кількості та ціни
                return quantity * unitPrice;
            }

            // Якщо парсинг не вдався — повертаємо 0,
            // що запобігає аварійному завершенню імпорту
            return 0m;
        }
    }

    // -----------------------------------------------------
    // Клас мапінгу CSV-колонок на поля сутності Transaction
    // Забезпечує коректне зчитування та підготовку даних
    public sealed class TransactionMap : ClassMap<Transaction>
    {
        public TransactionMap()
        {
            // 1. Мапінг дати транзакції
            Map(m => m.Date).Name("InvoiceDate");

            // 2. Мапінг ідентифікатора клієнта
            Map(m => m.CustomerId).Name("CustomerID");

            // 3. Мапінг ідентифікатора товару
            Map(m => m.ProductId).Name("StockCode");

            Map(m => m.ProductName).Name("Description");

            // 4. Мапінг кількості придбаних одиниць
            Map(m => m.Quantity).Name("Quantity");

            // 5. Мапінг ціни за одиницю товару
            Map(m => m.UnitPrice).Name("UnitPrice");

            // 6. Обчислення доходу через власний конвертер
            // Значення Revenue не зчитується напряму з CSV,
            // а обчислюється автоматично під час імпорту
            Map(m => m.Revenue)
                .Name("UnitPrice")
                .TypeConverter<RevenueConverter>();
        }
    }

}
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
        // Метод імпорту транзакцій з CSV-файлу, який тепер приймає додатковий параметр userId для прив'язки даних до конкретного користувача
        public async Task<int> ImportTransactionsFromCsvAsync(Stream fileStream, int userId) // Додали userId
        {
            // Створення ізольованого Scope для безпечного керування життєвим циклом контексту БД
            using (var scope = _serviceProvider.CreateScope())
            {
                // Отримання екземпляра контексту бази даних з поточного контейнера залежностей
                var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
                // Налаштування конфігурації парсера CSV з урахуванням наявності рядка заголовків
                var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };

                // Ініціалізація потокового зчитування текстового файлу
                using var reader = new StreamReader(fileStream);
                // Створення об'єкта CsvReader для синтаксичного розбору завантаженого файлу
                using var csv = new CsvReader(reader, config);

                // Отримання глобальних налаштувань конвертації типів для дати та часу
                var options = csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>();
                // Реєстрація масиву підтримуваних шаблонів текстових форматів дати
                options.Formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" };
                // Підключення розробленого класу мапінгу колонок файлу на сутність Transaction
                csv.Context.RegisterClassMap<TransactionMap>();

                try
                {
                    // Потокове зчитування всіх записів з файлу та їх конвертація в список об'єктів
                    var transactions = csv.GetRecords<Transaction>().ToList();

                    // ПРИВ'ЯЗКА: кожній транзакції призначаємо власника
                    foreach (var t in transactions)
                    {
                        // Присвоєння транзакції ідентифікатора поточного авторизованого користувача
                        t.UserId = userId;
                    }

                    // Асинхронне додавання пакету зчитаних транзакцій у буфер контексту EF Core
                    await context.Transactions.AddRangeAsync(transactions);
                    // Збереження змін у СУБД PostgreSQL та повернення кількості записаних рядків
                    return await context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Перехоплення помилок парсингу та генерація виключення з описом проблеми
                    throw new InvalidOperationException($"Помилка: {ex.Message}");
                }
            }
        }
        // Метод для швидкого очищення попередніх даних користувача перед імпортом нових транзакцій
        public async Task ClearPreviousDataAsync(int userId)
        {
            // Створення окремого Scope для виконання операцій швидкого очищення сховища
            using (var scope = _serviceProvider.CreateScope())
            {
                // Отримання контексту бази даних з поточного Scope розробки
                var context = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

                // Швидке видалення прямо в БД (ExecuteDelete)
                await context.Transactions
                    .Where(t => t.UserId == userId)
                    .ExecuteDeleteAsync(); // Миттєве каскадне видалення транзакцій користувача на рівні СКБД

                await context.SavedAnalyses
                    .Where(a => a.UserId == userId)
                    .ExecuteDeleteAsync(); // Миттєве видалення застарілих кешованих результатів ML-моделей

                // SaveChangesAsync тут вже не потрібен для ExecuteDelete, 
                // бо команда виконується миттєво
            }
        }
    }

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

            // Якщо парсинг не вдався — повертаємо 0
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

            // Мапінг текстового опису найменування продукту
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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SalesAnalysis.Core.Entities;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SalesAnalysis.Tests
{
    [TestFixture]
    public class AnalysisServiceTests
    {
        private ServiceProvider _serviceProvider;
        private SalesDbContext _context; // Спільне посилання на контекст для SetUp та самих тестів

        [SetUp]
        public void SetUp()
        {
            var services = new ServiceCollection();

            // Створюємо ізольовану базу даних у пам'яті з унікальним ім'ям для кожного тест-кейсу
            services.AddDbContext<SalesDbContext>(o =>
                o.UseInMemoryDatabase("AnalysisTestDb_" + Guid.NewGuid().ToString()));

            _serviceProvider = services.BuildServiceProvider();

            // Ініціалізуємо єдиний контекст, який буде жити протягом усього тесту
            _context = _serviceProvider.GetRequiredService<SalesDbContext>();

            Seed();
        }

        [TearDown]
        public void TearDown()
        {
            _context?.Database.EnsureDeleted(); // Видаляємо базу з пам'яті
            _context?.Dispose();
            _serviceProvider?.Dispose();
        }

        private void Seed()
        {
            // Наповнюємо базу, використовуючи пряме посилання на поточний контекст
            _context.Transactions.AddRange(new[]
            {
                new Transaction { UserId = 1, CustomerId = "C1", ProductId = "P1", ProductName = "Тест", Revenue = 100.00m, Quantity = 1, UnitPrice = 100.00m, Date = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc) },
                new Transaction { UserId = 1, CustomerId = "C2", ProductId = "P2", ProductName = "Тест", Revenue = 200.00m, Quantity = 2, UnitPrice = 100.00m, Date = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc) },
                new Transaction { UserId = 1, CustomerId = "C1", ProductId = "P3", ProductName = "Тест", Revenue = 50.00m,  Quantity = 1, UnitPrice = 50.00m,  Date = new DateTime(2024, 2, 10, 0, 0, 0, DateTimeKind.Utc) }
            });
            _context.SaveChanges();
        }

        [Test]
        public async Task TotalRevenue_IsCalculatedCorrectly()
        {
            // Передаємо у сервіс той самий контекст, в який щойно записали сідові дані
            var service = new AnalysisService(_context);

            var result = await service.GetTotalRevenueAsync(1);
            Assert.AreEqual(350.00m, result);
        }

        [Test]
        public async Task MonthlyAggregation_FiltersUnfinishedMonth()
        {
            var service = new AnalysisService(_context);

            var data = await service.GetMonthlySalesDataAsync(1);

            Assert.AreEqual(1, data.Count);
            Assert.AreEqual(300f, data[0].SalesAmount);
            Assert.AreEqual(1f, data[0].MonthOfYear); // Перевіряємо, що залишився саме Січень
        }

        [Test]
        public async Task RfmData_ComputedCorrectly()
        {
            var service = new AnalysisService(_context);

            var rfm = await service.GetCustomerClusteringDataAsync(1);

            Assert.AreEqual(2, rfm.Count);

            // Глибока перевірка математики RFM-метрик для клієнта C1
            var customerC1 = rfm.FirstOrDefault(c => c.CustomerId == "C1");
            Assert.IsNotNull(customerC1);
            Assert.AreEqual(150.00f, customerC1.TotalSpent); // 100 + 50
            Assert.AreEqual(2, customerC1.PurchaseFrequency);  // 2 покупки

            // Останній запис у всьому датасеті: 2024-02-10. today = 2024-02-11.
            // Остання покупка C1: 2024-02-10. DaysSinceLastPurchase = 2024-02-11 - 2024-02-10 = 1 день.
            Assert.AreEqual(1.0f, customerC1.DaysSinceLastPurchase);
        }
    }
}
using Microsoft.EntityFrameworkCore.Storage;
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
        private InMemoryDatabaseRoot _dbRoot;

        [SetUp]
        public void SetUp()
        {
            _dbRoot = new InMemoryDatabaseRoot();
            var services = new ServiceCollection();

            services.AddDbContext<SalesDbContext>(o =>
                o.UseInMemoryDatabase("AnalysisDb", _dbRoot));

            _serviceProvider = services.BuildServiceProvider();
            Seed();
        }

        [TearDown]
        public void TearDown()
        {
            _serviceProvider.Dispose();
        }

        private void Seed()
        {
            using var scope = _serviceProvider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

            // ВИПРАВЛЕНО: Додано обов'язкові ProductId та ProductName, щоб база даних InMemory дозволила збереження
            ctx.Transactions.AddRange(new[]
            {
                // Січень 2024 (Повний місяць)
                new Transaction { UserId = 1, CustomerId = "C1", ProductId = "P1", ProductName = "Тест", Revenue = 100, Date = new DateTime(2024,1,10) },
                new Transaction { UserId = 1, CustomerId = "C2", ProductId = "P2", ProductName = "Тест", Revenue = 200, Date = new DateTime(2024,1,15) },
                // Лютий 2024 (Останній місяць у датасеті, автоматично відфільтровується алгоритмом як неповний)
                new Transaction { UserId = 1, CustomerId = "C1", ProductId = "P3", ProductName = "Тест", Revenue = 50,  Date = new DateTime(2024,2,10) }
            });
            ctx.SaveChanges();
        }

        [Test]
        public async Task TotalRevenue_IsCalculatedCorrectly()
        {
            var context = _serviceProvider.GetRequiredService<SalesDbContext>();
            var service = new AnalysisService(context);

            var result = await service.GetTotalRevenueAsync(1);
            Assert.AreEqual(350m, result); // Загальний дохід в базі (всі 3 транзакції) = 350
        }

        [Test]
        public async Task MonthlyAggregation_FiltersUnfinishedMonth()
        {
            var context = _serviceProvider.GetRequiredService<SalesDbContext>();
            var service = new AnalysisService(context);

            var data = await service.GetMonthlySalesDataAsync(1);

            // Лютий відфільтровано, залишився лише 1 повний місяць (Січень)
            Assert.AreEqual(1, data.Count);
            Assert.AreEqual(300f, data[0].SalesAmount); // 100 + 200
        }

        [Test]
        public async Task RfmData_ComputedCorrectly()
        {
            var context = _serviceProvider.GetRequiredService<SalesDbContext>();
            var service = new AnalysisService(context);

            var rfm = await service.GetCustomerClusteringDataAsync(1);
            Assert.AreEqual(2, rfm.Count); // Клієнти C1 та C2
        }
    }
}
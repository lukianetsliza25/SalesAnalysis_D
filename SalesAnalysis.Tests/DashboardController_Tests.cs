using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using SalesAnalysis.Core.Entities;
using SalesAnalysis.Core.Models;
using SalesAnalysis.Data;
using SalesAnalysis.Data.Services;
using SalesAnalysis.ML.Services;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace SalesAnalysis.Tests
{
    [TestFixture]
    public class DashboardControllerTests
    {
        private ServiceProvider _serviceProvider;
        private readonly int _testUserId = 1;

        private void SeedMinimumClusterData(SalesDbContext ctx)
        {
            // Наповнюємо базу транзакціями за ТРИ різні місяці:
            // Січень і Лютий система пропустить як повністю закриті, а Березень відфільтрує як неповний.
            ctx.Transactions.AddRange(new[]
            {
                // Січень 2024 (Разом 400 ₴)
                new Transaction { UserId = _testUserId, CustomerId = "C1", ProductId = "P1", Date = new DateTime(2024,1,1), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C1", ProductId = "P2", Date = new DateTime(2024,1,5), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C2", ProductId = "P3", Date = new DateTime(2024,1,2), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C2", ProductId = "P4", Date = new DateTime(2024,1,6), Revenue = 100, Quantity = 1, ProductName = "Test" },

                // Лютий 2024 (Разом 400 ₴)
                new Transaction { UserId = _testUserId, CustomerId = "C3", ProductId = "P5", Date = new DateTime(2024,2,3), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C3", ProductId = "P6", Date = new DateTime(2024,2,7), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C4", ProductId = "P7", Date = new DateTime(2024,2,4), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C4", ProductId = "P8", Date = new DateTime(2024,2,8), Revenue = 100, Quantity = 1, ProductName = "Test" },

                // Березень 2024 (Технічний неповний місяць — разом 50 ₴. Буде відфільтрований)
                new Transaction { UserId = _testUserId, CustomerId = "C1", ProductId = "P9", Date = new DateTime(2024,3,15), Revenue = 50,  Quantity = 1, ProductName = "Test" }
            });

            // Імітуємо готовий кеш штучного інтелекту в базі, щоб контролер не намагався писати файли моделей на диск
            var fakeClusters = new List<ClusteredCustomer>
            {
                new ClusteredCustomer { CustomerId = "C1", ClusterId = 2, ClusterDescription = "Постійний" },
                new ClusteredCustomer { CustomerId = "C2", ClusterId = 2, ClusterDescription = "Постійний" },
                new ClusteredCustomer { CustomerId = "C3", ClusterId = 2, ClusterDescription = "Постійний" },
                new ClusteredCustomer { CustomerId = "C4", ClusterId = 2, ClusterDescription = "Постійний" }
            };
            var fakeKpi = new List<MonthlyKpiData>
            {
                new MonthlyKpiData { MonthIndex = "2024-01", TotalRevenue = 400 },
                new MonthlyKpiData { MonthIndex = "2024-02", TotalRevenue = 400 }
            };

            ctx.SavedAnalyses.AddRange(new[]
            {
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "CustomerClusters", ResultJson = JsonSerializer.Serialize(fakeClusters), CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "MonthlyKpiHistory", ResultJson = JsonSerializer.Serialize(fakeKpi), CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "SalesForecastFastTree", ResultJson = "[100,120]", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "SalesForecastLinear", ResultJson = "[100,120]", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "BestModelName", ResultJson = "\"FastTree\"", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "R2FastTree", ResultJson = "0.9", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "R2Linear", ResultJson = "0.2", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "RmseFastTree", ResultJson = "100", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "RmseLinear", ResultJson = "200", CreatedAt = DateTime.UtcNow }
            });
        }

        [SetUp]
        public void SetUp()
        {
            var services = new ServiceCollection();
            services.AddDbContext<SalesDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddScoped<AnalysisService>();
            services.AddSingleton<ClusteringService>();
            services.AddSingleton<PredictionService>();
            _serviceProvider = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown() => _serviceProvider.Dispose();

        public DashboardController CreateController(Action<SalesDbContext> seed)
        {
            var ctx = _serviceProvider.GetRequiredService<SalesDbContext>();

            seed(ctx);
            ctx.SaveChanges();

            var store = new Mock<IUserStore<IdentityUser<int>>>();

            var mockUserManager = new Mock<UserManager<IdentityUser<int>>>(
                store.Object,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);

            mockUserManager
                .Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>()))
                .Returns(_testUserId.ToString());

            var controller = new DashboardController(
                _serviceProvider.GetRequiredService<AnalysisService>(),
                _serviceProvider.GetRequiredService<ClusteringService>(),
                _serviceProvider.GetRequiredService<PredictionService>(),
                mockUserManager.Object);

            var user = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString())
                ],
                "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user
                }
            };

            return controller;
        }

        [Test]
        public async Task Index_NoData_RedirectsToImport()
        {
            var controller = CreateController(ctx => { });
            var result = await controller.Index();

            Assert.IsInstanceOf<RedirectToActionResult>(result);
            var redirect = (RedirectToActionResult)result;
            Assert.AreEqual("Import", redirect.ControllerName);
        }

        [Test]
        public async Task Index_ComputesBasicKpi()
        {
            var controller = CreateController(SeedMinimumClusterData);

            var result = await controller.Index();
            Console.WriteLine(result.GetType().Name);

            Assert.IsInstanceOf<ViewResult>(result);

            Assert.That(controller.ViewBag.TotalRevenue, Is.Not.Null);
            Assert.That((decimal)controller.ViewBag.TotalRevenue, Is.EqualTo(850m));

            Assert.That(controller.ViewBag.TotalTransactions, Is.Not.Null);
            Assert.That((int)controller.ViewBag.TotalTransactions, Is.EqualTo(9));

            Assert.That(controller.ViewBag.UniqueCustomers, Is.Not.Null);
            Assert.That((int)controller.ViewBag.UniqueCustomers, Is.EqualTo(4));
        }
    }
}
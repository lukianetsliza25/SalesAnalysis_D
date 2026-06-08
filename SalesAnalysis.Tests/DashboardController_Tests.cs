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
        private SalesDbContext _dbContext;
        private readonly int _testUserId = 1;

        [SetUp]
        public void SetUp()
        {
            var services = new ServiceCollection();

            services.AddDbContext<SalesDbContext>(o => o.UseInMemoryDatabase("DashboardTestDb_" + Guid.NewGuid().ToString()));
            services.AddScoped<AnalysisService>();
            services.AddSingleton<ClusteringService>();
            services.AddSingleton<PredictionService>();

            _serviceProvider = services.BuildServiceProvider();
            _dbContext = _serviceProvider.GetRequiredService<SalesDbContext>();
        }

        [TearDown]
        public void TearDown()
        {
            _dbContext?.Database.EnsureDeleted();
            _dbContext?.Dispose(); // ВИПРАВЛЕНО: Явне звільнення ресурсів контексту БД усуває помилку NUnit1032
            _serviceProvider?.Dispose();
        }

        public DashboardController CreateController()
        {
            var store = new Mock<IUserStore<IdentityUser<int>>>();
            var mockUserManager = new Mock<UserManager<IdentityUser<int>>>(store.Object, null, null, null, null, null, null, null, null);

            mockUserManager
                .Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>()))
                .Returns(_testUserId.ToString());

            var controller = new DashboardController(
                _serviceProvider.GetRequiredService<AnalysisService>(),
                _serviceProvider.GetRequiredService<ClusteringService>(),
                _serviceProvider.GetRequiredService<PredictionService>(),
                mockUserManager.Object);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()) }, "TestAuth"));
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };

            return controller;
        }

        private void SeedMinimumClusterData()
        {
            _dbContext.Transactions.AddRange(new[]
            {
                new Transaction { UserId = _testUserId, CustomerId = "C1", ProductId = "P1", Date = new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc), Revenue = 100m, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C2", ProductId = "P3", Date = new DateTime(2024,1,2,0,0,0,DateTimeKind.Utc), Revenue = 100m, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C1", ProductId = "P9", Date = new DateTime(2024,2,15,0,0,0,DateTimeKind.Utc), Revenue = 50m,  Quantity = 1, ProductName = "Test" }
            });

            var fakeClusters = new List<ClusteredCustomer> { new ClusteredCustomer { CustomerId = "C1", ClusterId = 2, ClusterDescription = "Постійний" } };
            var fakeKpi = new List<MonthlyKpiData> { new MonthlyKpiData { MonthIndex = "2024-01", TotalRevenue = 200 } };

            _dbContext.SavedAnalyses.AddRange(new[]
            {
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "CustomerClusters", ResultJson = JsonSerializer.Serialize(fakeClusters), CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "MonthlyKpiHistory", ResultJson = JsonSerializer.Serialize(fakeKpi), CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "SalesForecastFastTree", ResultJson = "[100,120]", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "SalesForecastLinear", ResultJson = "[100,120]", CreatedAt = DateTime.UtcNow },
                new SavedAnalysis { UserId = _testUserId, ProductId = "ALL", AnalysisType = "BestModelName", ResultJson = "\"FastTree\"", CreatedAt = DateTime.UtcNow }
            });

            _dbContext.SaveChanges();
        }

        [Test]
        //
        public async Task Index_NoData_RedirectsToImport()
        {
            // Не додаємо жодних транзакцій, щоб імітувати відсутність даних
            var controller = CreateController();
            // Викликаємо метод Index і очікуємо перенаправлення на Import
            var result = await controller.Index();

            // Перевіряємо, що результат є перенаправленням на дію Import
            Assert.IsInstanceOf<RedirectToActionResult>(result);
            var redirect = (RedirectToActionResult)result;

            // Перевіряємо, що перенаправлення спрямоване на правильну дію та контролер
            Assert.AreEqual("Index", redirect.ActionName);
            // Враховуючи, що Import знаходиться в тому ж контролері,
            // перевіряємо лише назву контролера
            Assert.AreEqual("Import", redirect.ControllerName);
        }

        [Test]
        //
        public async Task Index_WithCachedData_ReturnsViewWithKpi()
        {
            // Наповнюємо базу мінімальними даними для успішного
            // виконання кластеризації та KPI аналізу
            SeedMinimumClusterData();
            var controller = CreateController();

            // Викликаємо метод Index і очікуємо повернення представлення з KPI даними
            var result = await controller.Index();

            // Перевіряємо, що результат є представленням і що KPI дані передані у ViewBag
            Assert.IsInstanceOf<ViewResult>(result);
            // Враховуючи, що ми зберегли один місяць з загальним доходом 200, перевіряємо ці значення
            Assert.AreEqual(250m, controller.ViewBag.TotalRevenue);
            // Ми зберегли 3 транзакції, тому очікуємо, що загальна кількість транзакцій буде 3
            Assert.AreEqual(3, controller.ViewBag.TotalTransactions);
        }
    }
}
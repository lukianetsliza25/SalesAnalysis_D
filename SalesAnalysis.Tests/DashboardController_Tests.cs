using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity; // ДОДАНО
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
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
using System.Linq;
using System.Security.Claims; // ДОДАНО для ClaimsPrincipal
using System.Threading.Tasks;

namespace SalesAnalysis.Tests
{
    [TestFixture]
    public class DashboardControllerTests
    {
        private ServiceProvider _serviceProvider;
        private readonly int _testUserId = 1; // ID нашого тестового користувача

        private void SeedMinimumClusterData(SalesDbContext ctx)
        {
            // Важливо: додаємо UserId = _testUserId до всіх транзакцій
            ctx.Transactions.AddRange(new[]
            {
                new Transaction { UserId = _testUserId, CustomerId = "C1", ProductId = "P1", Date = new DateTime(2024,1,1), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C1", ProductId = "P2", Date = new DateTime(2024,1,5), Revenue = 120, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C2", ProductId = "P3", Date = new DateTime(2024,1,2), Revenue = 90,  Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C2", ProductId = "P4", Date = new DateTime(2024,1,6), Revenue = 110, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C3", ProductId = "P5", Date = new DateTime(2024,1,3), Revenue = 95,  Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C3", ProductId = "P6", Date = new DateTime(2024,1,7), Revenue = 105, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C4", ProductId = "P7", Date = new DateTime(2024,1,4), Revenue = 100, Quantity = 1, ProductName = "Test" },
                new Transaction { UserId = _testUserId, CustomerId = "C4", ProductId = "P8", Date = new DateTime(2024,1,8), Revenue = 115, Quantity = 1, ProductName = "Test" }
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
            using (var scope = _serviceProvider.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
                seed(ctx);
                ctx.SaveChanges();
            }

            // Налаштування Mock для UserManager (Виправлено помилки CS0246)
            var store = new Mock<IUserStore<IdentityUser<int>>>();
            var mockUserManager = new Mock<UserManager<IdentityUser<int>>>(
                store.Object, null, null, null, null, null, null, null, null);

            // Налаштовуємо повернення ID = 1 (Виправлено CS1503: string -> int)
            mockUserManager.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>()))
                           .Returns(_testUserId.ToString());

            var controller = new DashboardController(
                _serviceProvider.GetRequiredService<AnalysisService>(),
                _serviceProvider.GetRequiredService<ClusteringService>(),
                _serviceProvider.GetRequiredService<PredictionService>(),
                mockUserManager.Object
            );

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        [Test]
        public async Task Index_NoData_RedirectsToImport()
        {
            var controller = CreateController(ctx => { });
            var result = await controller.Index();

            // Тепер, за вашою новою логікою, якщо даних немає — має бути Redirect
            Assert.IsInstanceOf<RedirectToActionResult>(result);
            var redirect = (RedirectToActionResult)result;
            Assert.AreEqual("Import", redirect.ControllerName);
        }

        [Test]
        public async Task Index_ComputesBasicKpi()
        {
            var controller = CreateController(ctx => SeedMinimumClusterData(ctx));
            await controller.Index();

            Assert.AreEqual(835m, (decimal)controller.ViewBag.TotalRevenue);
            Assert.AreEqual(8, (int)controller.ViewBag.TotalTransactions);
            Assert.AreEqual(4, (int)controller.ViewBag.UniqueCustomers);
        }
    }
}
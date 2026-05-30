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

        ctx.Transactions.AddRange(new[]
        {
            new Transaction { UserId = 1, CustomerId = "C1", Revenue = 100, Date = new DateTime(2024,1,10) },
            new Transaction { UserId = 1, CustomerId = "C1", Revenue = 50,  Date = new DateTime(2024,2,10) },
            new Transaction { UserId = 1, CustomerId = "C2", Revenue = 200, Date = new DateTime(2024,1,15) }
        });
        ctx.SaveChanges();
    }

    // Тест перевірки обчислення загального доходу
    [Test]
    public async Task TotalRevenue_IsCalculatedCorrectly()
    {
        // ВИПРАВЛЕНО: Передаємо безпосередньо контекст, а не провайдер
        var context = _serviceProvider.GetRequiredService<SalesDbContext>();
        var service = new AnalysisService(context);

        var result = await service.GetTotalRevenueAsync(1);
        Assert.AreEqual(350m, result);
    }

    // Тест перевірки агрегації місячних даних
    [Test]
    public async Task MonthlyAggregation_ReturnsCorrectCount()
    {
        // ВИПРАВЛЕНО: Передаємо безпосередньо контекст
        var context = _serviceProvider.GetRequiredService<SalesDbContext>();
        var service = new AnalysisService(context);

        var data = await service.GetMonthlySalesDataAsync(1);

        // Оскільки ми впровадили очищення від неповних місяців, лютий (останній місяць у Seed) відфільтровується.
        // Тому очікуємо 1 повний місяць (Січень).
        Assert.AreEqual(1, data.Count);
    }

    // Тест перевірки розрахунку RFM-метрик
    [Test]
    public async Task RfmData_ComputedCorrectly()
    {
        // ВИПРАВЛЕНО: Передаємо безпосередньо контекст
        var context = _serviceProvider.GetRequiredService<SalesDbContext>();
        var service = new AnalysisService(context);

        var rfm = await service.GetCustomerClusteringDataAsync(1);
        Assert.AreEqual(2, rfm.Count); // Клієнти C1 та C2
    }
}
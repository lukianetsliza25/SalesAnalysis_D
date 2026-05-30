using NUnit.Framework;
using SalesAnalysis.ML.Services;
using SalesAnalysis.Core.Models;
using System.Collections.Generic;

// Позначення класу як набору модульних тестів сервісу кластеризації
[TestFixture]
public class ClusteringServiceTests
{
    // Екземпляр сервісу кластеризації клієнтів
    private ClusteringService _service;

    // Ініціалізація сервісу перед кожним тестом
    [SetUp]
    public void SetUp()
    {
        _service = new ClusteringService();
    }

    // -----------------------------------------------------
    // Тест перевірки навчання моделі K-Means на коректних даних
    [Test]
    public void Model_Trains_OnValidData()
    {
        // Формування тестового набору RFM-даних клієнтів
        var data = new List<CustomerData>
        {
            new CustomerData
            {
                CustomerId = "C1",
                TotalSpent = 100,
                PurchaseFrequency = 2,
                DaysSinceLastPurchase = 10
            },
            new CustomerData
            {
                CustomerId = "C2",
                TotalSpent = 200,
                PurchaseFrequency = 3,
                DaysSinceLastPurchase = 20
            },
            new CustomerData
            {
                CustomerId = "C3",
                TotalSpent = 300,
                PurchaseFrequency = 4,
                DaysSinceLastPurchase = 5
            }
        };
        // Перетворення списку даних у IDataView для ML.NET
        var dv = _service.MLContext.Data.LoadFromEnumerable(data);
        // Перевірка, що навчання моделі не викликає винятків
        Assert.DoesNotThrow(() => _service.TrainAndSaveModel(dv));
    }

    // -----------------------------------------------------
    // Тест перевірки коректності ідентифікатора кластера
    [Test]
    public void Predict_AssignsValidClusterId()
    {
        // Формування набору клієнтів з подібними та відмінними RFM-показниками
        var customers = new List<CustomerData>
        {
            new CustomerData
            {
                TotalSpent = 100,
                PurchaseFrequency = 2,
                DaysSinceLastPurchase = 10
            },
            new CustomerData
            {
                TotalSpent = 105,
                PurchaseFrequency = 2,
                DaysSinceLastPurchase = 11
            },
            new CustomerData
            {
                TotalSpent = 500,
                PurchaseFrequency = 6,
                DaysSinceLastPurchase = 3
            }
        };
        // Навчання моделі кластеризації на тестових даних
        var model = _service.TrainAndSaveModel(
            _service.MLContext.Data.LoadFromEnumerable(customers));
        // Виконання прогнозу кластера для одного з клієнтів
        var prediction = _service.Predict(model, customers[0]);
        // Перевірка, що ідентифікатор кластера знаходиться у допустимому діапазоні
        Assert.That(prediction.PredictedClusterId, Is.InRange(1u, 3u));
    }
}

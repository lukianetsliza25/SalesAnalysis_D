using NUnit.Framework;
using SalesAnalysis.ML.Services;
using SalesAnalysis.Core.Models;
using System.Collections.Generic;

namespace SalesAnalysis.Tests
{
    [TestFixture]
    public class ClusteringServiceTests
    {
        private ClusteringService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new ClusteringService();
        }

        [Test]
        public void Model_Trains_OnValidData()
        {
            // Створюємо тестових клієнтів за допомогою актуального класу CustomerData
            var data = new List<CustomerData>
            {
                new CustomerData { CustomerId = "C1", TotalSpent = 100, PurchaseFrequency = 2, DaysSinceLastPurchase = 10 },
                new CustomerData { CustomerId = "C2", TotalSpent = 200, PurchaseFrequency = 3, DaysSinceLastPurchase = 20 },
                new CustomerData { CustomerId = "C3", TotalSpent = 300, PurchaseFrequency = 4, DaysSinceLastPurchase = 5 }
            };

            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            // Перевіряємо, що конвеєр логарифмування та навчання K-Means працює стабільно
            Assert.DoesNotThrow(() => _service.TrainAndSaveModel(dv));
        }

        [Test]
        public void Predict_AssignsValidClusterId()
        {
            var customers = new List<CustomerData>
            {
                new CustomerData { TotalSpent = 100, PurchaseFrequency = 2, DaysSinceLastPurchase = 10 },
                new CustomerData { TotalSpent = 105, PurchaseFrequency = 2, DaysSinceLastPurchase = 11 },
                new CustomerData { TotalSpent = 500, PurchaseFrequency = 6, DaysSinceLastPurchase = 3 }
            };

            var model = _service.TrainAndSaveModel(_service.MLContext.Data.LoadFromEnumerable(customers));
            var prediction = _service.Predict(model, customers[0]);

            // Перевіряємо, що ШІ видає адекватний номер купи (від 1 до 3 кластера)
            Assert.That(prediction.PredictedClusterId, Is.InRange(1u, 3u));
        }
    }
}
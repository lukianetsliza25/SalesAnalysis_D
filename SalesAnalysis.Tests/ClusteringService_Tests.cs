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
            var data = new List<CustomerData>
            {
                new CustomerData { CustomerId = "C1", TotalSpent = 100f, PurchaseFrequency = 2f, DaysSinceLastPurchase = 10f },
                new CustomerData { CustomerId = "C2", TotalSpent = 200f, PurchaseFrequency = 3f, DaysSinceLastPurchase = 20f },
                new CustomerData { CustomerId = "C3", TotalSpent = 300f, PurchaseFrequency = 4f, DaysSinceLastPurchase = 5f }
            };

            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            Assert.DoesNotThrow(() => _service.TrainAndSaveModel(dv));
        }

        [Test]
        public void Predict_AssignsValidClusterId()
        {
            var customers = new List<CustomerData>
            {
                new CustomerData { CustomerId = "1", TotalSpent = 100f, PurchaseFrequency = 2f, DaysSinceLastPurchase = 10f },
                new CustomerData { CustomerId = "2", TotalSpent = 105f, PurchaseFrequency = 2f, DaysSinceLastPurchase = 11f },
                new CustomerData { CustomerId = "3", TotalSpent = 500f, PurchaseFrequency = 6f, DaysSinceLastPurchase = 3f }
            };

            var dataView = _service.MLContext.Data.LoadFromEnumerable(customers);
            var model = _service.TrainAndSaveModel(dataView);
            var prediction = _service.Predict(model, customers[0]);

            // ВИПРАВЛЕНО: Індексація кластерів у ML.NET KMeans починається з 0u. 
            // Для 3-х кластерів валідними є значення 0, 1, 2.
            Assert.That(prediction.PredictedClusterId, Is.InRange(0u, 2u));
            Assert.IsNotNull(prediction.Distances);
            Assert.AreEqual(3, prediction.Distances.Length); // Перевіряємо, що масив відстаней відповідає кількості центроїдів
        }
    }
}
// SalesAnalysis.Tests/ClusteringService_Tests.cs
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
            // Створюємо тестові дані для тренування моделі
            var data = new List<CustomerData>
            {
                new CustomerData { 
                    CustomerId = "C1", // Унікальний ідентифікатор клієнта
                    TotalSpent = 100f, // Загальна сума витрат
                    PurchaseFrequency = 2f, // Частота покупок
                    DaysSinceLastPurchase = 10f // Дні з моменту останньої покупки
                },
                new CustomerData { 
                    CustomerId = "C2", // Унікальний ідентифікатор клієнта
                    TotalSpent = 200f, // Загальна сума витрат
                    PurchaseFrequency = 3f, // Частота покупок
                    DaysSinceLastPurchase = 20f // Дні з моменту останньої покупки
                },
                new CustomerData { 
                    CustomerId = "C3", // Унікальний ідентифікатор клієнта
                    TotalSpent = 300f, // Загальна сума витрат
                    PurchaseFrequency = 4f, // Частота покупок
                    DaysSinceLastPurchase = 5f // Дні з моменту останньої покупки
                }
            };
            // Завантажуємо дані у формат IDataView, який використовується для тренування моделі
            var dv = _service.MLContext.Data.LoadFromEnumerable(data);
            // Перевіряємо, що тренування моделі не викликає винятків на валідних даних
            Assert.DoesNotThrow(() => _service.TrainAndSaveModel(dv));
        }

        [Test]
        public void Predict_AssignsValidClusterId()
        {
            // Створюємо тестові дані для тренування моделі
            var customers = new List<CustomerData>
            {
                new CustomerData { CustomerId = "1", 
                    TotalSpent = 100f, // Загальна сума витрат
                    PurchaseFrequency = 2f, // Частота покупок
                    DaysSinceLastPurchase = 10f // Дні з моменту останньої покупки
                },
                new CustomerData { CustomerId = "2", 
                    TotalSpent = 105f, // Загальна сума витрат
                    PurchaseFrequency = 2f, // Частота покупок
                    DaysSinceLastPurchase = 11f // Дні з моменту останньої покупки
                },
                new CustomerData { CustomerId = "3", 
                    TotalSpent = 500f, // Загальна сума витрат
                    PurchaseFrequency = 6f, // Частота покупок
                    DaysSinceLastPurchase = 3f // Дні з моменту останньої покупки
                }
            };

            // Навчаємо модель на тестових даних та отримуємо прогноз для першого клієнта
            var dataView = _service.MLContext.Data.LoadFromEnumerable(customers);
            var model = _service.TrainAndSaveModel(dataView);
            var prediction = _service.Predict(model, customers[0]);

            // Перевіряємо, що прогнозований ClusterId знаходиться в
            // діапазоні допустимих значень (0, 1, 2)
            Assert.That(prediction.PredictedClusterId, Is.InRange(0u, 2u));
            // Перевіряємо, що масив відстаней до центроїдів не є null
            // і має довжину 3 (кількість кластерів)
            Assert.IsNotNull(prediction.Distances);
            // Перевіряємо, що масив відстаней відповідає кількості центроїдів
            Assert.AreEqual(3, prediction.Distances.Length);
        }
    }
}
using NUnit.Framework;
using SalesAnalysis.ML.Services;
using SalesAnalysis.Core.Models;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

namespace SalesAnalysis.Tests
{
    [TestFixture]
    public class PredictionServiceTests
    {
        private PredictionService _service;
        private readonly string _fastTreePath = "sales_prediction_model.zip";
        private readonly string _linearPath = "sales_linear_model.zip";

        [SetUp]
        public void SetUp()
        {
            _service = new PredictionService();
        }

        [TearDown]
        public void TearDown()
        {
            // Очищення дискового простору після кожного тесту убезпечує середовище розробки від сміттєвих файлів
            if (File.Exists(_fastTreePath)) File.Delete(_fastTreePath);
            if (File.Exists(_linearPath)) File.Delete(_linearPath);
        }

        [Test]
        public void Train_Throws_WhenLessThan4Points()
        {
            // Явно ініціалізуємо всі властивості моделі для забезпечення валідності структури схеми даних ML.NET
            var data = new List<SalesDataPoint>
            {
                new SalesDataPoint { TimeIndex = 1f, MonthOfYear = 1f, SalesAmount = 100f },
                new SalesDataPoint { TimeIndex = 2f, MonthOfYear = 2f, SalesAmount = 200f },
                new SalesDataPoint { TimeIndex = 3f, MonthOfYear = 3f, SalesAmount = 300f }
            };

            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            Assert.Throws<InvalidOperationException>(() => _service.TrainAndSaveModel(dv));
            Assert.Throws<InvalidOperationException>(() => _service.TrainAndSaveLinearModel(dv));
        }

        [Test]
        public void FastTree_And_LinearModels_TrainAndForecastCorrectly()
        {
            var data = new List<SalesDataPoint>
            {
                new SalesDataPoint { TimeIndex = 1f, MonthOfYear = 1f, SalesAmount = 100f },
                new SalesDataPoint { TimeIndex = 2f, MonthOfYear = 2f, SalesAmount = 150f },
                new SalesDataPoint { TimeIndex = 3f, MonthOfYear = 3f, SalesAmount = 200f },
                new SalesDataPoint { TimeIndex = 4f, MonthOfYear = 4f, SalesAmount = 250f }
            };

            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            var modelFastTree = _service.TrainAndSaveModel(dv);
            var modelLinear = _service.TrainAndSaveLinearModel(dv);

            // Перевірка працездатності ансамблевого методу градієнтного бустінгу (FastTree)
            var forecastFastTree = _service.PredictNPeriods(modelFastTree, startNextIndex: 5f, periods: 12, lastMonth: 4);
            Assert.AreEqual(12, forecastFastTree.Count);
            Assert.IsTrue(forecastFastTree.All(x => x >= 0f));

            // Перевірка працездатності лінійної регресії (алгоритм SDCA)
            var forecastLinear = _service.PredictNPeriods(modelLinear, startNextIndex: 5f, periods: 12, lastMonth: 4);
            Assert.AreEqual(12, forecastLinear.Count);
            Assert.IsTrue(forecastLinear.All(x => x >= 0f));

            // Додаткова архітектурна перевірка: підтверджуємо, що файли моделей були успішно фізично згенеровані на диску
            Assert.IsTrue(File.Exists(_fastTreePath));
            Assert.IsTrue(File.Exists(_linearPath));
        }
    }
}
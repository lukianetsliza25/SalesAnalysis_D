using NUnit.Framework;
using SalesAnalysis.ML.Services;
using SalesAnalysis.Core.Models;
using System.Collections.Generic;
using System;
using System.Linq;

namespace SalesAnalysis.Tests
{
    [TestFixture]
    public class PredictionServiceTests
    {
        private PredictionService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new PredictionService();
        }

        [Test]
        public void Train_Throws_WhenLessThan4Points()
        {
            var data = new List<SalesDataPoint>
            {
                new SalesDataPoint { TimeIndex = 1, SalesAmount = 100 },
                new SalesDataPoint { TimeIndex = 2, SalesAmount = 200 },
                new SalesDataPoint { TimeIndex = 3, SalesAmount = 300 }
            };

            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            // Перевіряємо захисні ліміти (мінімум 4 точки) для обох алгоритмів прогнозування
            Assert.Throws<InvalidOperationException>(() => _service.TrainAndSaveModel(dv));
            Assert.Throws<InvalidOperationException>(() => _service.TrainAndSaveLinearModel(dv));
        }

        [Test]
        public void FastTree_And_LinearModels_TrainAndForecastCorrectly()
        {
            var data = new List<SalesDataPoint>
            {
                new SalesDataPoint { TimeIndex = 1, MonthOfYear = 1, SalesAmount = 100 },
                new SalesDataPoint { TimeIndex = 2, MonthOfYear = 2, SalesAmount = 150 },
                new SalesDataPoint { TimeIndex = 3, MonthOfYear = 3, SalesAmount = 200 },
                new SalesDataPoint { TimeIndex = 4, MonthOfYear = 4, SalesAmount = 250 }
            };

            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            // Навчаємо обидві моделі
            var modelFastTree = _service.TrainAndSaveModel(dv);
            var modelLinear = _service.TrainAndSaveLinearModel(dv);

            // Перевіряємо прогноз гнучких дерев (FastTree)
            var forecastFastTree = _service.PredictNPeriods(modelFastTree, startNextIndex: 5, periods: 12, lastMonth: 4);
            Assert.AreEqual(12, forecastFastTree.Count);
            Assert.IsTrue(forecastFastTree.All(x => x >= 0));

            // Перевіряємо прогноз лінійної регресії (SDCA)
            var forecastLinear = _service.PredictNPeriods(modelLinear, startNextIndex: 5, periods: 12, lastMonth: 4);
            Assert.AreEqual(12, forecastLinear.Count);
            Assert.IsTrue(forecastLinear.All(x => x >= 0));
        }
    }
}
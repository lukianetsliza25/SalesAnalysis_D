// SalesAnalysis.Tests/PredictionService_Tests.cs
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
            // Створюємо тестові дані з менш ніж 4 точками, що є
            // мінімально необхідною кількістю для тренування моделей
            var data = new List<SalesDataPoint>
            {
                new SalesDataPoint { 
                    TimeIndex = 1f, // Перший період (наприклад, січень) з індексом 1
                    MonthOfYear = 1f, // Календарний номер місяця (1 для січня) для виявлення сезонності
                    SalesAmount = 100f // Загальна сума виторгу за цей період (цільова мітка Label)
                },
                new SalesDataPoint { 
                    TimeIndex = 2f, // Другий період (лютий) з індексом 2
                    MonthOfYear = 2f, // Календарний номер місяця (2 для лютого) для виявлення сезонності
                    SalesAmount = 200f // Загальна сума виторгу за цей період (цільова мітка Label)
                },
                new SalesDataPoint { 
                    TimeIndex = 3f, // Третій період (березень) з індексом 3
                    MonthOfYear = 3f, // Календарний номер місяця (3 для березня) для виявлення сезонності
                    SalesAmount = 300f // Загальна сума виторгу за цей період (цільова мітка Label)
                }
            };

            // Завантажуємо дані у формат IDataView, який використовується для тренування моделі
            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            // Перевіряємо, що тренування моделей на недостатній кількості даних викликає очікувані винятки
            Assert.Throws<InvalidOperationException>(() => _service.TrainAndSaveModel(dv));
            Assert.Throws<InvalidOperationException>(() => _service.TrainAndSaveLinearModel(dv));
        }

        [Test]
        public void FastTree_And_LinearModels_TrainAndForecastCorrectly()
        {
            var data = new List<SalesDataPoint>
            {
                new SalesDataPoint { 
                    TimeIndex = 1f,// Перший період (наприклад, січень) з індексом 1
                    MonthOfYear = 1f, // Календарний номер місяця (1 для січня) для виявлення сезонності
                    SalesAmount = 100f // Загальна сума виторгу за цей період (цільова мітка Label)
                },
                new SalesDataPoint { 
                    TimeIndex = 2f,// Другий період (лютий) з індексом 2
                    MonthOfYear = 2f,// Календарний номер місяця (2 для лютого) для виявлення сезонності
                    SalesAmount = 150f // Загальна сума виторгу за цей період (цільова мітка Label) 
                },
                new SalesDataPoint { 
                    TimeIndex = 3f,// Третій період (березень) з індексом 3
                    MonthOfYear = 3f, // Календарний номер місяця (3 для березня) для виявлення сезонності
                    SalesAmount = 200f // Загальна сума виторгу за цей період (цільова мітка Label)
                },
                new SalesDataPoint { 
                    TimeIndex = 4f, // Четвертий період (квітень) з індексом 4
                    MonthOfYear = 4f, // Календарний номер місяця (4 для квітня) для виявлення сезонності
                    SalesAmount = 250f // Загальна сума виторгу за цей період (цільова мітка Label)
                }
            };
            // Завантажуємо дані у формат IDataView, який використовується для тренування моделі
            var dv = _service.MLContext.Data.LoadFromEnumerable(data);

            var modelFastTree = _service.TrainAndSaveModel(dv);
            var modelLinear = _service.TrainAndSaveLinearModel(dv);

            // Перевірка працездатності ансамблевого методу градієнтного бустінгу (FastTree)
            var forecastFastTree = _service.PredictNPeriods(
                modelFastTree, 
                startNextIndex: 5f, 
                periods: 12, 
                lastMonth: 4
                );
            // Очікуємо 12 прогнозів для наступних 12 місяців, починаючи з індексу 5 (травень)
            Assert.AreEqual(12, forecastFastTree.Count);
            Assert.IsTrue(forecastFastTree.All(x => x >= 0f));

            // Перевірка працездатності лінійної регресії (алгоритм SDCA)
            var forecastLinear = _service.PredictNPeriods(
                modelLinear, 
                startNextIndex: 5f, 
                periods: 12, 
                lastMonth: 4
                );
            // Очікуємо 12 прогнозів для наступних 12 місяців, починаючи з індексу 5 (травень)
            Assert.AreEqual(12, forecastLinear.Count);
            Assert.IsTrue(forecastLinear.All(x => x >= 0f));

            // Додаткова архітектурна перевірка: підтверджуємо, що файли моделей
            // були успішно фізично згенеровані на диску
            Assert.IsTrue(File.Exists(_fastTreePath));
            Assert.IsTrue(File.Exists(_linearPath));
        }
    }
}
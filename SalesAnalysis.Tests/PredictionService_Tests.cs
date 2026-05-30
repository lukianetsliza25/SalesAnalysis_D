using NUnit.Framework;
using SalesAnalysis.ML.Services;
using SalesAnalysis.Core.Models;
using System.Collections.Generic;
using System;
using System.Linq;

[TestFixture]
public class PredictionServiceTests
{
    private PredictionService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new PredictionService();
    }

    // Тест перевірки валідації мінімальної кількості даних
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

        Assert.Throws<InvalidOperationException>(() =>
            _service.TrainAndSaveModel(dv));
    }

    // Тест перевірки коректності прогнозу на декілька періодів
    [Test]
    public void Forecast_ReturnsCorrectCount_AndNonNegative()
    {
        var data = new List<SalesDataPoint>
        {
            new SalesDataPoint { TimeIndex = 1, MonthOfYear = 1, SalesAmount = 100 },
            new SalesDataPoint { TimeIndex = 2, MonthOfYear = 2, SalesAmount = 150 },
            new SalesDataPoint { TimeIndex = 3, MonthOfYear = 3, SalesAmount = 200 },
            new SalesDataPoint { TimeIndex = 4, MonthOfYear = 4, SalesAmount = 250 }
        };

        var model = _service.TrainAndSaveModel(
            _service.MLContext.Data.LoadFromEnumerable(data));

        // ВИПРАВЛЕНО: Додано четвертий параметр lastMonth (передаємо 4)
        var forecast = _service.PredictNPeriods(model, startNextIndex: 5, periods: 12, lastMonth: 4);

        Assert.AreEqual(12, forecast.Count);
        Assert.IsTrue(forecast.All(x => x >= 0));
    }
}
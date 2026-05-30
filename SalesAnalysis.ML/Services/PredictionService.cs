// SalesAnalysis.ML/Services/PredictionService.cs
using Microsoft.ML;
using SalesAnalysis.Core.Models;
using Microsoft.ML.Data;
using System.Linq;
using System.Collections.Generic;
using System;

namespace SalesAnalysis.ML.Services
{
    public class PredictionService
    {
        // Контекст ML.NET з фіксованим seed для відтворюваності результатів
        public MLContext MLContext { get; } = new MLContext(seed: 0);

        // Шлях до файлу збереженої моделі прогнозування
        private const string ModelPath = "sales_prediction_model.zip";

        // -----------------------------------------------------
        // Метод навчання регресійної моделі та збереження її на диск
        public ITransformer TrainAndSaveModel(IDataView trainingData)
        {
            if (trainingData.GetRowCount() < 4)
                throw new InvalidOperationException("Недостатньо точок даних для навчання.");

            var pipeline = MLContext.Transforms.Concatenate("Features",
                    nameof(SalesDataPoint.TimeIndex),
                    nameof(SalesDataPoint.MonthOfYear)) // Додано другу ознаку
                .Append(MLContext.Transforms.NormalizeMinMax("Features"))
                .Append(MLContext.Regression.Trainers.FastTree(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfTrees: 100));

            var model = pipeline.Fit(trainingData);
            MLContext.Model.Save(model, trainingData.Schema, ModelPath);
            return model;
        }

        // Оновіть також метод PredictNPeriods, щоб він вираховував правильний MonthOfYear для майбутнього
        // Оновлений метод PredictNPeriods
        public List<float> PredictNPeriods(ITransformer trainedModel, float startNextIndex, int periods, int lastMonth)
        {
            var results = new List<float>();
            var predictionEngine = MLContext.Model.CreatePredictionEngine<SalesDataPoint, SalesPrediction>(trainedModel);

            for (int i = 0; i < periods; i++)
            {
                var nextTimeIndex = startNextIndex + i;

                // Розрахунок місяця року: (поточний + крок) % 12
                var nextMonthOfYear = ((lastMonth - 1 + 1 + i) % 12) + 1;

                var input = new SalesDataPoint
                {
                    TimeIndex = nextTimeIndex,
                    MonthOfYear = (float)nextMonthOfYear
                };

                var prediction = predictionEngine.Predict(input);
                // Додаємо результат (не менше 0)
                results.Add((float)Math.Round(Math.Max(0, prediction.PredictedSales), 2));
            }
            return results;
        }

        // -----------------------------------------------------
        // Метод прогнозування одного наступного періоду

        public SalesPrediction Predict(
            ITransformer trainedModel,
            float nextTimeIndex)
        {
            // Створення PredictionEngine для виконання прогнозу
            var predictionEngine = MLContext.Model
                .CreatePredictionEngine<SalesDataPoint, SalesPrediction>(
                    trainedModel);

            // Формування вхідних даних для прогнозування
            var input = new SalesDataPoint
            {
                TimeIndex = nextTimeIndex,
                MonthOfYear = 1
            };

            // Повернення прогнозного значення
            return predictionEngine.Predict(input);
        }

        // -----------------------------------------------------
        // Метод прогнозування продажів на N майбутніх періодів
        
    }
}

// SalesAnalysis.ML/Services/PredictionService.cs
using Microsoft.ML;
using SalesAnalysis.Core.Models;
using Microsoft.ML.Data;
using System.Collections.Generic;
using System;

namespace SalesAnalysis.ML.Services
{
    public class PredictionService
    {
        // Головний пульт керування штучним інтелектом (MLContext)
        public MLContext MLContext { get; } = new MLContext(seed: 0);

        // Назви двох різних файлів, які будуть створюватися на диску
        private const string FastTreeModelPath = "sales_prediction_model.zip";
        private const string LinearModelPath = "sales_linear_model.zip";

        // -----------------------------------------------------
        // 1. Метод навчання першої моделі (Гнучкі дерева — FastTree)
        public ITransformer TrainAndSaveModel(IDataView trainingData)
        {
            if (trainingData.GetRowCount() < 4)
                throw new InvalidOperationException("Недостатньо точок даних для навчання FastTree.");

            // Конвеєр підготовки: збираємо індекс часу та місяць року разом
            var pipeline = MLContext.Transforms.Concatenate("Features",
                    nameof(SalesDataPoint.TimeIndex),
                    nameof(SalesDataPoint.MonthOfYear))
                .Append(MLContext.Transforms.NormalizeMinMax("Features"))
                // Навчаємо гнучкі дерева FastTree
                .Append(MLContext.Regression.Trainers.FastTree(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfTrees: 100));

            var model = pipeline.Fit(trainingData);

            // Зберігаємо першу модель на диск
            MLContext.Model.Save(model, trainingData.Schema, FastTreeModelPath);
            return model;
        }

        // -----------------------------------------------------
        // 2. Метод навчання другої моделі (Пряма лінія — Лінійна регресія)
        public ITransformer TrainAndSaveLinearModel(IDataView trainingData)
        {
            if (trainingData.GetRowCount() < 4)
                throw new InvalidOperationException("Недостатньо точок даних для навчання лінійної регресії.");

            // Точно такий же конвеєр підготовки, щоб усе було чесно
            var pipeline = MLContext.Transforms.Concatenate("Features",
                    nameof(SalesDataPoint.TimeIndex),
                    nameof(SalesDataPoint.MonthOfYear))
                .Append(MLContext.Transforms.NormalizeMinMax("Features"))
                // Навчаємо класичну лінійну регресію (алгоритм SDCA)
                .Append(MLContext.Regression.Trainers.Sdca(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    maximumNumberOfIterations: 100));

            var model = pipeline.Fit(trainingData);

            // Зберігаємо другу модель на диск під іншим ім'ям
            MLContext.Model.Save(model, trainingData.Schema, LinearModelPath);
            return model;
        }

        // -----------------------------------------------------
        // 3. Метод прогнозування на 12 місяців наперед (Підходить для обох моделей)
        public List<float> PredictNPeriods(ITransformer trainedModel, float startNextIndex, int periods, int lastMonth)
        {
            var results = new List<float>();
            var predictionEngine = MLContext.Model.CreatePredictionEngine<SalesDataPoint, SalesPrediction>(trainedModel);

            for (int i = 0; i < periods; i++)
            {
                var nextTimeIndex = startNextIndex + i;
                var nextMonthOfYear = ((lastMonth - 1 + 1 + i) % 12) + 1;

                var input = new SalesDataPoint
                {
                    TimeIndex = nextTimeIndex,
                    MonthOfYear = (float)nextMonthOfYear
                };

                var prediction = predictionEngine.Predict(input);

                // Округлюємо результат і страхуємося від мінусів (лінійна лінія іноді може падати нижче нуля)
                results.Add((float)Math.Round(Math.Max(0, prediction.PredictedSales), 2));
            }
            return results;
        }

        // -----------------------------------------------------
        // 4. Одиночний прогноз (для сумісності, якщо десь викликається)
        public SalesPrediction Predict(ITransformer trainedModel, float nextTimeIndex)
        {
            var predictionEngine = MLContext.Model.CreatePredictionEngine<SalesDataPoint, SalesPrediction>(trainedModel);
            var input = new SalesDataPoint { TimeIndex = nextTimeIndex, MonthOfYear = 1 };
            return predictionEngine.Predict(input);
        }
    }
}
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
        // Контекст ML.NET із фіксованим seed для забезпечення повторюваності результатів
        public MLContext MLContext { get; } = new MLContext(seed: 0);

        // Шляхи до файлів серіалізованих предиктивних моделей на локальному диску
        private const string FastTreeModelPath = "sales_prediction_model.zip";
        private const string LinearModelPath = "sales_linear_model.zip";

        // 1. Метод навчання першої моделі (Гнучкі дерева — FastTree)
        public ITransformer TrainAndSaveModel(IDataView trainingData)
        {
            // Алгоритм FastTree потребує мінімум 4 крапки часового ряду
            if (trainingData.GetRowCount() < 4)
                throw new InvalidOperationException(
                    "Недостатньо точок даних для навчання FastTree.");

            // Конкатенація індексу часу та номеру місяця у вектор Features
            var pipeline = MLContext.Transforms.Concatenate("Features",
                    nameof(SalesDataPoint.TimeIndex),
                    nameof(SalesDataPoint.MonthOfYear))
                // Лінійна нормалізація Min-Max для приведення вхідних ознак до масштабу [0, 1]
                .Append(MLContext.Transforms.NormalizeMinMax("Features"))
                // Навчання 100 регресійних дерев рішення за алгоритмом градієнтного бустингу
                .Append(MLContext.Regression.Trainers.FastTree(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfTrees: 100));

            // Запуск ітераційного процесу навчання моделі на підготовленому наборі даних
            var model = pipeline.Fit(trainingData);

            // Збереження навченого ансамблю FastTree у ZIP-архів на локальний диск
            MLContext.Model.Save(model, trainingData.Schema, FastTreeModelPath);
            return model;
        }

        // 2. Метод навчання другої моделі (Лінійна регресія)
        public ITransformer TrainAndSaveLinearModel(IDataView trainingData)
        {
            // лінійний регресор потребує мінімум 4 крапки часового ряду
            if (trainingData.GetRowCount() < 4)
                throw new InvalidOperationException(
                    "Недостатньо точок даних для навчання лінійної регресії.");

            // Побудова ідентичного конвеєра ознак
            var pipeline = MLContext.Transforms.Concatenate("Features",
                    nameof(SalesDataPoint.TimeIndex),
                    nameof(SalesDataPoint.MonthOfYear))
                // Нормалізація вхідних факторів
                .Append(MLContext.Transforms.NormalizeMinMax("Features"))
                // Навчання лінійної регресії (SDCA)
                .Append(MLContext.Regression.Trainers.Sdca(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    maximumNumberOfIterations: 100));

            // Обчислення параметрів лінійної функції на історичних даних
            var model = pipeline.Fit(trainingData);

            // Збереження навченої лінійної моделі у файл під окремим ім'ям
            MLContext.Model.Save(model, trainingData.Schema, LinearModelPath);
            return model;
        }

        // 3. Метод прогнозування на 12 місяців наперед (Підходить для обох моделей)
        public List<float> PredictNPeriods(ITransformer trainedModel, float startNextIndex, int periods, int lastMonth)
        {
            var results = new List<float>();
            // Ініціалізація PredictionEngine для розрахунку прогнозних значень у поточному потоці
            var predictionEngine = MLContext.Model.CreatePredictionEngine<SalesDataPoint, SalesPrediction>(trainedModel);

            // Циклічна генерація точок предиктивного ряду на задану кількість кроків наперед
            for (int i = 0; i < periods; i++)
            {
                // Послідовне нарощування майбутнього часового індексу для кожної ітерації
                var nextTimeIndex = startNextIndex + i;
                // Розрахунок циклічного календарного номеру майбутнього місяця (діапазон 1-12) для сезонності
                var nextMonthOfYear = ((lastMonth - 1 + 1 + i) % 12) + 1;

                // Створення об'єкта вхідних ознак для передачі обчислювальному двигуну
                var input = new SalesDataPoint
                {
                    TimeIndex = nextTimeIndex,
                    MonthOfYear = (float)nextMonthOfYear
                };

                // Розрахунок точкового предиктивного значення обраною моделлю машинного навчання
                var prediction = predictionEngine.Predict(input);

                // Коригування результату (обнулення мінусів при падінні лінійного тренду) та округлення до копійок
                results.Add((float)Math.Round(Math.Max(0, prediction.PredictedSales), 2));
            }
            return results;
        }

        // 4. Одиночний прогноз (для сумісності, якщо десь викликається)
        public SalesPrediction Predict(ITransformer trainedModel, float nextTimeIndex)
        {
            // Ініціалізація PredictionEngine для генерації одного предиктивного скаляра
            var predictionEngine = MLContext.Model.CreatePredictionEngine<SalesDataPoint, SalesPrediction>(trainedModel);
            // Ініціалізація точки вхідних даних із базовим значенням місяця
            var input = new SalesDataPoint { TimeIndex = nextTimeIndex, MonthOfYear = 1 };
            return predictionEngine.Predict(input);
        }
    }
}
// SalesAnalysis.ML/Services/ClusteringService.cs
using Microsoft.ML;
using Microsoft.ML.Data;
using SalesAnalysis.Core.Models;
using System;

namespace SalesAnalysis.ML.Services
{
    public class ClusteringService
    {
        // Контекст ML.NET із фіксованим seed для відтворюваності результатів
        public MLContext MLContext { get; } = new MLContext(seed: 1);

        // Шлях до файлу збереженої моделі кластеризації
        private const string ModelPath = "customer_clustering_model.zip";

        // Метод навчання моделі кластеризації та збереження її на диск
        public ITransformer TrainAndSaveModel(IDataView data)
        {
            // Формування конвеєра обробки даних та конфігурація алгоритму
            var pipeline =
                // 1. Логарифмічне перетворення RFM-ознак для стабілізації розподілу
                MLContext.Transforms.CustomMapping<CustomerData, RfmLogTransformed>(
                    (input, output) =>
                    {
                        output.TotalSpent =
                            (float)Math.Log(1 + input.TotalSpent);

                        output.PurchaseFrequency =
                            (float)Math.Log(1 + input.PurchaseFrequency);

                        output.DaysSinceLastPurchase =
                            (float)Math.Log(1 + input.DaysSinceLastPurchase);
                    },
                    // Контракт для ідентифікації кастомного перетворення всередині конвеєра
                    contractName: "LogMap"
                )

                // 2. Нормалізація ознак за методом Min-Max для приведення до масштабу [0, 1]
                .Append(MLContext.Transforms.NormalizeMinMax(
                    "SpentNorm", nameof(RfmLogTransformed.TotalSpent)))

                .Append(MLContext.Transforms.NormalizeMinMax(
                    "FreqNorm", nameof(RfmLogTransformed.PurchaseFrequency)))

                .Append(MLContext.Transforms.NormalizeMinMax(
                    "RecNorm", nameof(RfmLogTransformed.DaysSinceLastPurchase)))

                // 3. Об’єднання нормалізованих ознак у єдиний вектор Features
                .Append(MLContext.Transforms.Concatenate(
                    "Features", "SpentNorm", "FreqNorm", "RecNorm"))

                // 4. Навчання моделі кластеризації K-Means на 3 цільові групи
                .Append(MLContext.Clustering.Trainers.KMeans(
                    "Features", numberOfClusters: 3));

            var model = pipeline.Fit(data);

            // Збереження навченої моделі у файл для запобігання повторним обчисленням
            MLContext.Model.Save(model, data.Schema, ModelPath);

            return model;
        }

        // -----------------------------------------------------
        // Метод прогнозування кластера для окремого клієнта
        public CustomerClusterPrediction Predict(
            ITransformer model,
            CustomerData customer)
        {
            // Ініціалізація PredictionEngine для виконання поштучної класифікації
            var engine = MLContext.Model.CreatePredictionEngine<
                CustomerData, CustomerClusterPrediction>(model);

            return engine.Predict(customer);
        }

        // -----------------------------------------------------
        // Внутрішня модель для зберігання логарифмічно перетворених RFM-ознак
        public class RfmLogTransformed
        {
            public float TotalSpent { get; set; }

            public float PurchaseFrequency { get; set; }

            public float DaysSinceLastPurchase { get; set; }
        }
    }
}
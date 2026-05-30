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

        // -----------------------------------------------------
        // Метод навчання моделі кластеризації та збереження її на диск
        public ITransformer TrainAndSaveModel(IDataView data)
        {
            // Формування конвеєра обробки даних та навчання моделі кластеризації
            var pipeline =
                // 1. Логарифмічне перетворення RFM-ознак
                MLContext.Transforms.CustomMapping<CustomerData, RfmLogTransformed>(
                    (input, output) =>
                    {
                        // Логарифмування сумарних витрат клієнта (Monetary)
                        output.TotalSpent =
                            (float)Math.Log(1 + input.TotalSpent);

                        // Логарифмування частоти покупок (Frequency)
                        output.PurchaseFrequency =
                            (float)Math.Log(1 + input.PurchaseFrequency);

                        // Логарифмування давності останньої покупки (Recency)
                        output.DaysSinceLastPurchase =
                            (float)Math.Log(1 + input.DaysSinceLastPurchase);
                    },
                    // Ім’я контракту, необхідне для внутрішньої ідентифікації трансформації
                    contractName: "LogMap"
                )

                // 2. Нормалізація ознак за методом Min-Max
                // Забезпечує приведення значень до єдиного масштабу
                .Append(MLContext.Transforms.NormalizeMinMax(
                    "SpentNorm", nameof(RfmLogTransformed.TotalSpent)))

                .Append(MLContext.Transforms.NormalizeMinMax(
                    "FreqNorm", nameof(RfmLogTransformed.PurchaseFrequency)))

                .Append(MLContext.Transforms.NormalizeMinMax(
                    "RecNorm", nameof(RfmLogTransformed.DaysSinceLastPurchase)))

                // 3. Об’єднання нормалізованих ознак у вектор Features
                .Append(MLContext.Transforms.Concatenate(
                    "Features", "SpentNorm", "FreqNorm", "RecNorm"))

                // 4. Навчання моделі кластеризації K-Means
                // Кількість кластерів встановлено рівною 3
                .Append(MLContext.Clustering.Trainers.KMeans(
                    "Features", numberOfClusters: 3));

            // Навчання моделі на вхідних даних
            var model = pipeline.Fit(data);

            // Збереження навченої моделі у файл
            MLContext.Model.Save(model, data.Schema, ModelPath);

            return model;
        }

        // -----------------------------------------------------
        // Метод прогнозування кластера для окремого клієнта
        public CustomerClusterPrediction Predict(
            ITransformer model,
            CustomerData customer)
        {
            // Створення PredictionEngine для виконання прогнозу
            var engine = MLContext.Model.CreatePredictionEngine<
                CustomerData, CustomerClusterPrediction>(model);

            // Повернення результату кластеризації клієнта
            return engine.Predict(customer);
        }

        // -----------------------------------------------------
        // Внутрішня модель для зберігання логарифмічно перетворених RFM-ознак
        public class RfmLogTransformed
        {
            // Логарифмоване значення сумарних витрат клієнта
            public float TotalSpent { get; set; }

            // Логарифмована частота покупок
            public float PurchaseFrequency { get; set; }

            // Логарифмована давність останньої покупки
            public float DaysSinceLastPurchase { get; set; }
        }
    }
}

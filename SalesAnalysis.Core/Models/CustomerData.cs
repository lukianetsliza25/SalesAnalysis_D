// SalesAnalysis.Core/Models/CustomerData.cs
using Microsoft.ML.Data;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

// Модель для агрегованої точки часового ряду
[NotMapped]
public class CustomerData
{
    // CustomerId, необхідний для відображення у таблиці
    public string CustomerId { get; set; }

    // Вхідні дані для моделі K-Means
    [LoadColumn(0)]
    public float TotalSpent { get; set; }
    [LoadColumn(1)]
    public float PurchaseFrequency { get; set; }
    [LoadColumn(2)]
    public float DaysSinceLastPurchase { get; set; }
}

[NotMapped]
public class CustomerClusterPrediction
{
    [ColumnName("PredictedLabel")]
    public uint PredictedClusterId { get; set; }

    [ColumnName("Score")]
    public float[] Distances { get; set; }
}
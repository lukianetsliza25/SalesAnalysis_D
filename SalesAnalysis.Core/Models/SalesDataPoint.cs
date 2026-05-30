// SalesAnalysis.Core/Models/SalesDataPoint.cs
using Microsoft.ML.Data;
using System.ComponentModel.DataAnnotations.Schema;


[NotMapped]
public class SalesDataPoint
{
    public float TimeIndex { get; set; }

    public float MonthOfYear { get; set; }

    [ColumnName("Label")]
    public float SalesAmount { get; set; }
}

[NotMapped]
public class SalesPrediction
{
    [ColumnName("Score")]
    public float PredictedSales { get; set; }
}
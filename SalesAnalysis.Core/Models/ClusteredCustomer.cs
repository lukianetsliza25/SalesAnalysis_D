// SalesAnalysis.Core/Models/ClusteredCustomer.cs
namespace SalesAnalysis.Core.Models
{
    public class ClusteredCustomer
    {
        public string CustomerId { get; set; }
        public float TotalSpent { get; set; }
        public float PurchaseFrequency { get; set; }
        public int ClusterId { get; set; } // ID кластера, визначений ML-моделлю
        public string ClusterDescription { get; set; } // Опис для UI
    }
}

// SalesAnalysis.Core/Entities/Transaction.cs
using System.ComponentModel.DataAnnotations;

namespace SalesAnalysis.Core.Entities
{
    public class Transaction
    {
        public int Id { get; set; }
        public int UserId { get; set; } // ДОДАТИ ЦЕЙ РЯДОК
        public DateTime Date { get; set; }

        public string ProductId { get; set; }
        public string ProductName { get; set; }

        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal Revenue { get; set; }

        public string CustomerId { get; set; }
    }
}

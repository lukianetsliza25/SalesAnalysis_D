// SalesAnalysis.Core/Models/MonthlyKpiData.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SalesAnalysis.Core.Models
{
    public class MonthlyKpiData
    {
        public string MonthIndex { get; set; }
        public float TotalRevenue { get; set; }
        public int TotalTransactions { get; set; }
        public int UniqueCustomers { get; set; }

        public float AverageOrderValue { get; set; }
        public float CustomerSpend { get; set; }
        public float Frequency { get; set; }
    }
}

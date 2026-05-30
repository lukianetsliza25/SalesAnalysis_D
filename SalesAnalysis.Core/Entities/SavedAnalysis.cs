// SalesAnalysis.Core/Entities/SavedAnalysis.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SalesAnalysis.Core.Entities
{
    public class SavedAnalysis
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        public string ProductId { get; set; }

        public string AnalysisType { get; set; }

        public string ResultJson { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
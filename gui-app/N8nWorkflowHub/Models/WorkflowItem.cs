using System;
using System.Collections.Generic;

namespace N8nWorkflowHub.Models
{
    public class WorkflowItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public string NodesSummary { get; set; } = string.Empty;
        
        public int NodeCount
        {
            get
            {
                if (string.IsNullOrWhiteSpace(NodesSummary)) return 0;
                return NodesSummary.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }

        public string DisplayNodes
        {
            get
            {
                if (string.IsNullOrWhiteSpace(NodesSummary)) return "General Nodes";
                var items = NodesSummary.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (items.Length <= 4) return string.Join(" • ", items);
                return $"{string.Join(" • ", items.AsSpan(0, 4).ToArray())} +{items.Length - 4} more";
            }
        }
    }

    public class CategoryItem
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public string DisplayText => $"{Name} ({Count:N0})";
    }
}

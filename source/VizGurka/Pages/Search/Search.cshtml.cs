using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SpecGurka.GurkaSpec;
using System.Collections.Generic;
using System.Linq;
using VizGurka.Helpers;

namespace VizGurka.Pages.Search
{
    public class SearchModel : PageModel
    {
        //private readonly IStringLocalizer<SearchModel> _localizer;
        public List<Feature> Features { get; set; } = new List<Feature>();
        public List<Scenario> Scenarios { get; set; } = new List<Scenario>();
        public Guid Id { get; set; } = Guid.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public List<Feature> FeatureSearchResults { get; set; } = new List<Feature>();
        public List<ScenarioWithFeatureId> ScenarioSearchResults { get; set; } = new List<ScenarioWithFeatureId>();
        public DateTime LatestRunDate { get; set; } = DateTime.MinValue;
        public Guid FirstFeatureId { get; set; } = Guid.Empty;
        public int FeatureResultCount { get; set; } = 0;
        public int ScenarioResultCount { get; set; } = 0;

        public class ScenarioWithFeatureId
        {
            public Guid FeatureId { get; set; } = Guid.Empty;
            public Scenario? Scenario { get; set; }
            public List<Step> Steps { get; set; } = new List<Step>();
        }

        public void OnGet(string productName, string query)
        {
            ProductName = productName;
            Query = query;
            var latestRun = TestrunReader.ReadLatestRun(productName);
            var product = latestRun?.Products.FirstOrDefault();
            if (product != null)
            {
                PopulateFeatures(product);
                PopulateScenarios();
                if (Features.Any())
                {
                    FirstFeatureId = Features.First().Id; // Set the Id of the first feature
                }
            }

            if (latestRun != null)
            {
                LatestRunDate = DateTime.Parse(latestRun.DateAndTime);
            }

            if (!string.IsNullOrEmpty(Query))
            {
                // The logic for searching for features and scenarios is inside this if statement
                if (product != null)
                {
                    // Count features that match the query by name or have scenarios that match the query
                    FeatureResultCount = product.Features
                        .Count(f => f.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                                    f.Scenarios.Any(s => s.Name.Contains(Query, StringComparison.OrdinalIgnoreCase)) ||
                                    f.Rules.Any(r => r.Scenarios.Any(s => s.Name.Contains(Query, StringComparison.OrdinalIgnoreCase))));

                    // Include features that match the query by name or scenarios that match the query
                    FeatureSearchResults = product.Features
                        .Where(f => f.Name.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                                    f.Scenarios.Any(s => s.Name.Contains(Query, StringComparison.OrdinalIgnoreCase)) ||
                                    f.Rules.Any(r => r.Scenarios.Any(s => s.Name.Contains(Query, StringComparison.OrdinalIgnoreCase))))
                        .ToList();

                    // Include scenarios that match the query
                    ScenarioSearchResults = product.Features
                        .SelectMany(f => f.Scenarios
                            .Where(s => s.Name.Contains(Query, StringComparison.OrdinalIgnoreCase))
                            .Select(s => new ScenarioWithFeatureId
                            {
                                FeatureId = f.Id,
                                Scenario = s,
                                Steps = s.Steps
                            })
                            .Concat(f.Rules.SelectMany(r => r.Scenarios
                                .Where(s => s.Name.Contains(Query, StringComparison.OrdinalIgnoreCase))
                                .Select(s => new ScenarioWithFeatureId
                                {
                                    FeatureId = f.Id,
                                    Scenario = s,
                                    Steps = s.Steps
                                }))))
                        .ToList();

                    ScenarioResultCount = ScenarioSearchResults.Count;
                }
            }
        }

        private void PopulateFeatures(SpecGurka.GurkaSpec.Product product)
        {
            Features = product.Features.Select(f => new Feature
            {
                Id = f.Id,
                Name = f.Name,
                Status = f.Status,
                Scenarios = f.Scenarios,
                Rules = f.Rules,
                Description = f.Description
            }).ToList();
        }

        private void PopulateScenarios()
        {
            Scenarios = Features.SelectMany(f => f.Scenarios.Concat(f.Rules.SelectMany(r => r.Scenarios))).ToList();
        }
    }
}
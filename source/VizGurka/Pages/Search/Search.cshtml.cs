using System.Net;
using System.Text.RegularExpressions;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Markdig;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using VizGurka.Services;

namespace VizGurka.Pages.Search;

public class SearchModel : PageModel
{
    private readonly IStringLocalizer<SearchModel> _localizer;
    private readonly LuceneIndexService _luceneIndexService;
    private readonly ILogger<SearchModel> _logger;
    private readonly Dictionary<string, string> _fieldPrefixMappings;

    public SearchModel(IStringLocalizer<SearchModel> localizer, LuceneIndexService luceneIndexService, ILogger<SearchModel> logger)
    {
        _localizer = localizer;
        _luceneIndexService = luceneIndexService;
        _logger = logger;
        
        // Initialize the mapping dictionary for field prefixes
        _fieldPrefixMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "feature:", "FeatureName:" },
            { "egenskap:", "FeatureName:" },
            { "feature name:", "FeatureName:" },
            { "feature id:", "FeatureId:" },
            { "feature description:", "FeatureDescription:" },
            { "feature status:", "FeatureStatus:" },

            { "scenario:", "ScenarioName:" },
            { "test:", "ScenarioName:" },
            { "scenario name:", "ScenarioName:" },
            { "scenario status:", "ScenarioStatus:" },
            { "scenario duration:", "ScenarioTestDuration:" },

            { "step:", "StepText:" },
            { "steg:", "StepText:" },

            { "tag:", "Tag:" },
            { "tags:", "Tag:" },
            { "@", "Tag:" },
            { "featuretag:", "FeatureTag:" },
            { "feature tag:", "FeatureTag:" },
            { "scenariotag:", "ScenarioTag:" },
            { "scenario tag:", "ScenarioTag:" },
            {"rule:", "RuleName:" },
            { "rule description:", "RuleDescription:" },
            { "rule status:", "RuleStatus:" },
            { "ruletag:", "RuleTag:" },
            { "rule tag:", "RuleTag:" },

            { "file:", "FileName:" },
            { "fil:", "FileName:" },
            { "name:", "Name:" },
            { "namn:", "Name:" },
            { "branch:", "BranchName:" },
            { "commit:", "CommitId:" },
            { "message:", "CommitMessage:" },

            { "parent:", "ParentFeatureId:" },
            { "parent feature:", "ParentFeatureId:" },
            { "parent name:", "ParentFeatureName:" }
        };
    }

    public string ProductName { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public List<SearchResult> SearchResults { get; set; } = new();
    public Guid FirstFeatureId { get; set; } = Guid.Empty;

    public MarkdownPipeline Pipeline { get; set; } = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public void OnGet(string productName, string query)
    {
        ProductName = productName;
        Query = WebUtility.UrlDecode(query);

        if (!string.IsNullOrEmpty(Query)) PerformLuceneSearch();
    }

    private void PerformLuceneSearch()
    {
        try
        {
            string processedQuery = ApplyFieldPrefixMappings(Query);
            if (Query.StartsWith("@", StringComparison.OrdinalIgnoreCase))
            {
                processedQuery = "Tag:" + Query.Substring(1);
            }
            
            processedQuery = ApplyFieldPrefixMappings(processedQuery);
            
            _logger.LogInformation($"Original query: {Query}");
            _logger.LogInformation($"Processed query: {processedQuery}");

            var directory = _luceneIndexService.GetIndexDirectory();
            using var reader = DirectoryReader.Open(directory);
            var searcher = new IndexSearcher(reader);

            var analyzer = _luceneIndexService.GetAnalyzer();

            string[] fields = {
                "FileName", "Name", "BranchName", "CommitId", "CommitMessage",
                "FeatureName", "FeatureDescription", "FeatureStatus", "FeatureId",
                "ScenarioName", "ScenarioStatus", "ScenarioTestDuration",
                "ParentFeatureId", "ParentFeatureName", "StepText",
                "Tag", "FeatureTag", "ScenarioTag", "RuleTag", "RuleName", "RuleDescription", "RuleStatus"
            };

            var boosts = new Dictionary<string, float> {
                { "FileName", 1.0f },
                { "Name", 1.5f },
                { "BranchName", 1.0f },
                { "CommitId", 1.0f },
                { "CommitMessage", 1.0f },
                { "FeatureName", 2.0f },
                { "FeatureDescription", 1.5f },
                { "FeatureStatus", 1.0f },
                { "FeatureId", 1.0f },
                { "ScenarioName", 1.8f },
                { "ScenarioStatus", 1.0f },
                { "ScenarioTestDuration", 1.0f },
                { "ParentFeatureId", 1.0f },
                { "ParentFeatureName", 1.0f },
                { "StepText", 1.5f },
                { "Tag", 2.0f },
                { "FeatureTag", 1.5f },
                { "ScenarioTag", 1.5f },
                { "RuleTag", 1.0f },
                { "RuleName", 1.8f },
                { "RuleDescription", 1.2f },
                { "RuleStatus", 1.0f }
            };

            if (!HasExplicitFieldPrefix(processedQuery))
            {
                var multiFieldQueryParser = new MultiFieldQueryParser(
                    LuceneVersion.LUCENE_48,
                    fields,
                    analyzer,
                    boosts
                );
                
                try
                {
                    var query = multiFieldQueryParser.Parse(processedQuery);
                    ExecuteSearch(searcher, query);
                }
                catch (ParseException)
                {
                    string escapedQuery = QueryParser.Escape(processedQuery);
                    var fallbackQuery = multiFieldQueryParser.Parse(escapedQuery);
                    ExecuteSearch(searcher, fallbackQuery);
                }
            }
            else
            {
                var queryParser = new QueryParser(LuceneVersion.LUCENE_48, "FeatureName", analyzer);
                try
                {
                    var query = queryParser.Parse(processedQuery);
                    ExecuteSearch(searcher, query);
                }
                catch (ParseException)
                {
                    _logger.LogWarning($"Error parsing explicit field query: {processedQuery}");
                    
                    string[] parts = processedQuery.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        string fieldName = parts[0].Trim();
                        string searchTerm = QueryParser.Escape(parts[1].Trim());
                        
                        var fallbackQuery = new TermQuery(new Term(fieldName, searchTerm));
                        ExecuteSearch(searcher, fallbackQuery);
                    }
                    else
                    {
                        string escapedQuery = QueryParser.Escape(processedQuery);
                        var fallbackQueryParser = new QueryParser(LuceneVersion.LUCENE_48, "FeatureName", analyzer);
                        var fallbackQuery = fallbackQueryParser.Parse(escapedQuery);
                        ExecuteSearch(searcher, fallbackQuery);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during search: {Message}", ex.Message);
            SearchResults = new List<SearchResult>();
        }
    }

    private void ExecuteSearch(IndexSearcher searcher, Query query)
    {
        var hits = searcher.Search(query, 10).ScoreDocs;
        
        SearchResults = hits.Select(hit =>
        {
            var doc = searcher.Doc(hit.Doc);
            
            string docType = DetermineDocumentType(doc);
            
            var sourceField = DetermineMatchingField(doc, Query);
            
            var result = new SearchResult
            {
                Title = doc.Get("FeatureName") ?? doc.Get("ScenarioName") ?? doc.Get("Name") ?? "No Title",
                Content = doc.Get("FeatureDescription") ?? doc.Get("StepText") ?? doc.Get("CommitMessage") ?? "No Content",
                Score = hit.Score,
                SourceField = sourceField,
                DocumentType = docType,
                FileName = doc.Get("FileName") ?? "No File"
            };
            
            var tags = new List<string>();
            foreach (var tagField in doc.GetFields("Tag"))
            {
                if (!string.IsNullOrEmpty(tagField.GetStringValue()))
                {
                    tags.Add(tagField.GetStringValue());
                }
            }
            result.Tags = tags.Distinct().ToList();
            
            if (docType == "Feature")
            {
                result.FeatureId = doc.Get("FeatureId") ?? string.Empty;
                
                var featureTags = new List<string>();
                foreach (var tagField in doc.GetFields("FeatureTag"))
                {
                    if (!string.IsNullOrEmpty(tagField.GetStringValue()))
                    {
                        featureTags.Add(tagField.GetStringValue());
                    }
                }
                result.TypeSpecificTags = featureTags;
            }
            else if (docType == "Scenario")
            {
                result.ParentFeatureId = doc.Get("ParentFeatureId") ?? string.Empty;
                result.ParentFeatureName = doc.Get("ParentFeatureName") ?? string.Empty;
                
                if (!string.IsNullOrEmpty(result.ParentFeatureId) && 
                    Guid.TryParse(result.ParentFeatureId, out Guid featureId))
                {
                    if (FirstFeatureId == Guid.Empty)
                    {
                        FirstFeatureId = featureId;
                    }
                }

                var scenarioTags = new List<string>();
                foreach (var tagField in doc.GetFields("ScenarioTag"))
                {
                    if (!string.IsNullOrEmpty(tagField.GetStringValue()))
                    {
                        scenarioTags.Add(tagField.GetStringValue());
                    }
                }
                result.TypeSpecificTags = scenarioTags;
            }

            if (docType == "Feature" || docType == "Scenario")
            {
                result.Status = doc.Get(docType + "Status") ?? string.Empty;
            }
            
            if (docType == "Scenario")
            {
                result.Duration = doc.Get("ScenarioTestDuration") ?? string.Empty;
            }

            return result;
        }).ToList();
        
        if (FirstFeatureId == Guid.Empty && SearchResults.Any())
        {
            var firstWithFeatureId = SearchResults.FirstOrDefault(r => !string.IsNullOrEmpty(r.FeatureId));
            if (firstWithFeatureId != null && Guid.TryParse(firstWithFeatureId.FeatureId, out Guid featureId))
            {
                FirstFeatureId = featureId;
            }
            else
            {
                var firstWithParentId = SearchResults.FirstOrDefault(r => !string.IsNullOrEmpty(r.ParentFeatureId));
                if (firstWithParentId != null && Guid.TryParse(firstWithParentId.ParentFeatureId, out Guid parentId))
                {
                    FirstFeatureId = parentId;
                }
            }
        }
    }
    
    private string ApplyFieldPrefixMappings(string query)
    {
        if (string.IsNullOrEmpty(query))
            return query;
        
        foreach (var mapping in _fieldPrefixMappings)
        {
            if (query.StartsWith(mapping.Key, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.Value + query.Substring(mapping.Key.Length);
            }
        }
        
        return query;
    }
    
    private bool HasExplicitFieldPrefix(string query)
    {
        if (string.IsNullOrEmpty(query))
            return false;
        
        var match = System.Text.RegularExpressions.Regex.Match(query, @"^\s*([A-Za-z]+[A-Za-z0-9]*):\s*");
        return match.Success;
    }

    private string DetermineDocumentType(Document doc)
    {
        if (doc.Get("ScenarioName") != null)
        {
            return "Scenario";
        }
        else if (doc.Get("FeatureName") != null)
        {
            return "Feature";
        }
        else if (doc.Get("CommitMessage") != null)
        {
            return "Testrun";
        }
        return "Unknown";
    }

    private string DetermineMatchingField(Document doc, string query)
    {
        if (query.StartsWith("@", StringComparison.OrdinalIgnoreCase))
        {
            string tagValue = query.Substring(1);
            
            foreach (var field in new[] { "Tag", "FeatureTag", "ScenarioTag", "RuleTag" })
            {
                foreach (var tagField in doc.GetFields(field))
                {
                    string storedTagValue = tagField.GetStringValue();
                    if (!string.IsNullOrEmpty(storedTagValue) &&
                        (storedTagValue.Equals(tagValue, StringComparison.OrdinalIgnoreCase) ||
                         storedTagValue.Equals("@" + tagValue, StringComparison.OrdinalIgnoreCase)))
                    {
                        return field;
                    }
                }
            }
        }

        var match = System.Text.RegularExpressions.Regex.Match(query, @"^\s*([A-Za-z]+[A-Za-z0-9]*):\s*(.+)$");
        if (match.Success)
        {
            string fieldPrefix = match.Groups[1].Value.Trim();
            
            if (fieldPrefix.Equals("tag", StringComparison.OrdinalIgnoreCase) ||
                fieldPrefix.Equals("tags", StringComparison.OrdinalIgnoreCase))
            {
                return "Tag";
            }
            
            foreach (var mapping in _fieldPrefixMappings)
            {
                if (mapping.Key.StartsWith(fieldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string fieldName = mapping.Value.TrimEnd(':');
                    
                    if (doc.Get(fieldName) != null)
                    {
                        return fieldName;
                    }
                }
            }
        }
        
        string[] fieldsToCheck = {
            "Tag", "ScenarioTag", "FeatureTag", "RuleTag",
            "ScenarioName", "FeatureName", "FeatureDescription", "StepText", 
            "CommitMessage", "Name", "FileName", "BranchName", "CommitId","RuleName", "RuleDescription", "RuleStatus"
        };
        
        string searchTerm = query;
        if (match.Success)
        {
            searchTerm = match.Groups[2].Value.Trim();
        }


        foreach (var field in fieldsToCheck)
        {
            if (field.EndsWith("Tag"))
            {
                foreach (var tagField in doc.GetFields(field))
                {
                    string tagValue = tagField.GetStringValue();
                    if (!string.IsNullOrEmpty(tagValue) && 
                        tagValue.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return field;
                    }
                }
            }
            else
            {
                string fieldValue = doc.Get(field);
                if (!string.IsNullOrEmpty(fieldValue) && 
                    fieldValue.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return field;
                }
            }
        }

        string[] queryWords = searchTerm.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?' }, 
                                        StringSplitOptions.RemoveEmptyEntries);
        
        if (queryWords.Length > 0)
        {
            var fieldMatches = new Dictionary<string, int>();
            
            foreach (var field in fieldsToCheck)
            {
                if (field.EndsWith("Tag"))
                {
                    int matchCount = 0;
                    foreach (var tagField in doc.GetFields(field))
                    {
                        string tagValue = tagField.GetStringValue();
                        if (!string.IsNullOrEmpty(tagValue))
                        {
                            foreach (var word in queryWords)
                            {
                                if (word.Length > 2 &&
                                    tagValue.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    matchCount++;
                                }
                            }
                        }
                    }
                    
                    if (matchCount > 0)
                    {
                        fieldMatches[field] = matchCount;
                    }
                }
                else
                {
                    string fieldValue = doc.Get(field);
                    if (!string.IsNullOrEmpty(fieldValue))
                    {
                        int matchCount = 0;
                        foreach (var word in queryWords)
                        {
                            if (word.Length > 2 &&
                                fieldValue.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matchCount++;
                            }
                        }
                        
                        if (matchCount > 0)
                        {
                            fieldMatches[field] = matchCount;
                        }
                    }
                }
            }
            
            if (fieldMatches.Count > 0)
            {
                return fieldMatches.OrderByDescending(kv => kv.Value).First().Key;
            }
        }

        string docType = DetermineDocumentType(doc);
        switch (docType)
        {
            case "Feature":
                return "FeatureName";
            case "Scenario":
                return "ScenarioName";
            case "Testrun":
                return "CommitMessage";
            default:
                return "Unknown";
        }
    }

    public IHtmlContent MarkdownStringToHtml(string input)
    {
        if (string.IsNullOrEmpty(input))
            return new HtmlString(string.Empty);
            
        var trimmedInput = input.Trim();
        return new HtmlString(Markdown.ToHtml(trimmedInput, Pipeline));
    }

    public class SearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public float Score { get; set; }
        public string SourceField { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> TypeSpecificTags { get; set; } = new List<string>();
        
        public string FeatureId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        
        public string ParentFeatureId { get; set; } = string.Empty;
        public string ParentFeatureName { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        
        public string FileName { get; set; } = string.Empty;
        public string GetRelevantFeatureId()
        {
            if (DocumentType == "Feature")
                return FeatureId;
            else if (DocumentType == "Scenario")
                return ParentFeatureId;
            return string.Empty;
        }
    }
}
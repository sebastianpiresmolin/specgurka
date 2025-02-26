using Microsoft.AspNetCore.Mvc.RazorPages;
using VizGurka.Helpers;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace VizGurka.Pages;

public class IndexModel : PageModel
{
    private readonly IStringLocalizer<IndexModel> _localizer;
    public IndexModel(IStringLocalizer<IndexModel> localizer)
    {
        _localizer = localizer;
    }
    public List<(string ProductName, DateTime LatestRunDate, Guid Id)> UniqueProductNamesWithDatesAndId { get; set; } = new List<(string ProductName, DateTime LatestRunDate, Guid Id)>();

    public void OnGet()
    {
        // this is just for logging purposes
        var currentCulture = CultureInfo.CurrentCulture.Name;
        Console.WriteLine($"Current Culture: {currentCulture}");

        var uniqueProductNames = TestrunReader.GetUniqueProductNames();

        foreach (var productName in uniqueProductNames)
        {
            var latestRun = TestrunReader.ReadLatestRun(productName);
            if (latestRun == null) continue;

            var testRunDateTime = DateTime.Parse(latestRun.RunDate, CultureInfo.InvariantCulture);
            var product = latestRun.Products.FirstOrDefault(p => p.Name == productName);
            if (product == null) continue;

            var feature = product.Features.FirstOrDefault();
            if (feature == null) continue;

            UniqueProductNamesWithDatesAndId.Add((productName, testRunDateTime, feature.Id));
        }

        UniqueProductNamesWithDatesAndId = UniqueProductNamesWithDatesAndId
            .OrderByDescending(item => item.LatestRunDate)
            .ToList();

    }
}
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ZiApp.Domain.TaxReports;

namespace ZiApp.Application.Reports;

public static class AnnualSummarySnapshot
{
    public const string SchemaVersion = "ziapp-annual-preparation-v1";
    public const int MaxReports = 20;
    public const int MaxPortfolios = 1000;
    public const int MaxBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static AnnualResult<AnnualSummaryDocument> Read(AnnualPreparationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.SchemaVersion != SchemaVersion || Encoding.UTF8.GetByteCount(draft.SnapshotJson) > MaxBytes
            || ReportSnapshot.Hash(draft.SnapshotJson) != draft.SnapshotSha256)
        { return new(null, AnnualSummaryError.InvalidSnapshot); }
        try
        {
            var document = JsonSerializer.Deserialize<AnnualSummaryDocument>(draft.SnapshotJson, Options);
            if (document is null || document.Id != draft.Id || document.OwnerAccountId != draft.OwnerAccountId
                || document.CreatedByAccountId != draft.OwnerAccountId || document.TaxYear != draft.TaxYear
                || document.CreatedAtUtc != draft.CreatedAtUtc || document.SchemaVersion != draft.SchemaVersion
                || document.Status != "Draft" || document.IsTaxReady || document.Inputs is null
                || document.SourceReports is null || document.PortfolioInventory is null
                || document.OmittedPortfolioIds is null || document.SelectedReportsSubtotal is null
                || document.ReviewIssues is null || !AnnualSummaryBuilder.ValidInput(document.Inputs))
            { return new(null, AnnualSummaryError.InvalidSnapshot); }
            return new(document);
        }
        catch (JsonException) { return new(null, AnnualSummaryError.InvalidSnapshot); }
    }
}

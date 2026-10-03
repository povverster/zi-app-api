using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ZiApp.Domain.TaxReports;

namespace ZiApp.Application.Reports;

public static class ReportSnapshot
{
    public const string SchemaVersion = "ziapp-draft-report-v1";
    public const string YearPolicyVersion = "broker-calendar-year-v1";
    public const string RoundingPolicyVersion = "full-decimal-no-filing-rounding-v1";
    public const int MaxTrades = 10000;
    public const int MaxSplits = 10000;
    public const int MaxBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string InputHash(ReportInputs inputs) => Hash(Serialize(inputs));

    public static ReportResult<TaxReportDocument> Read(TaxCalculationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.SnapshotJson is null) { return new(null, ReportError.LegacySnapshotUnavailable); }
        if (run.ReportSchemaVersion != SchemaVersion || Hash(run.SnapshotJson) != run.SnapshotSha256)
        { return new(null, ReportError.InvalidSnapshot); }
        try
        {
            var document = JsonSerializer.Deserialize<TaxReportDocument>(run.SnapshotJson, Options);
            if (document is null || document.Id != run.Id || document.PortfolioId != run.PortfolioId
                || document.TaxYear != run.TaxYear || document.CreatedAtUtc != run.CreatedAtUtc
                || document.CalculationVersion != run.CalculationVersion || document.SchemaVersion != run.ReportSchemaVersion
                || document.InputSha256 != run.InputSha256 || document.Inputs is null
                || InputHash(document.Inputs) != run.InputSha256 || document.IsTaxReady || document.Status != "Draft")
            { return new(null, ReportError.InvalidSnapshot); }
            return new(document);
        }
        catch (JsonException) { return new(null, ReportError.InvalidSnapshot); }
    }
}

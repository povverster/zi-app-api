using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ZiApp.Application.Ledger;
using ZiApp.Application.Trading;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;

namespace ZiApp.Application.Reports;

public static class ConfiguredTaxSnapshot
{
    public const string SchemaVersion = "ziapp-configured-tax-report-v1";
    public const int MaxBytes = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool ValidSettings(SaveTaxSettingsInput input) => input is not null
        && TryRate(input.InvestmentIncomePercent, out _) && TryRate(input.MilitaryPercent, out _)
        && TryRate(input.DividendIncomePercent, out _);

    public static bool TryRate(string? text, out decimal value) =>
        TradingValidation.TryAmount(text, true, out value) && value <= 100m
        && (!text!.Contains('.', StringComparison.Ordinal) || text.Length - text.IndexOf('.', StringComparison.Ordinal) - 1 <= 4);

    public static decimal Amount(string text)
    {
        if (text is null || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal value) || LedgerDecimal.Format(value) != text)
        { throw new FormatException("An exact canonical decimal amount is required."); }
        return value;
    }

    public static ConfiguredTaxResult<ConfiguredTaxDocument> Build(Guid owner, TaxCalculationRun run,
        TaxSettingsRevision settings, DateTimeOffset captured)
    {
        var source = ReportSnapshot.Read(run);
        if (source.Value is null)
        {
            return new(null, source.Error == ReportError.LegacySnapshotUnavailable
                ? ConfiguredTaxError.LegacySnapshotUnavailable : ConfiguredTaxError.InvalidSource);
        }
        var report = source.Value;
        if (settings.OwnerAccountId != owner) { return new(null, ConfiguredTaxError.NotFound); }
        if (settings.TaxYear != report.TaxYear) { return new(null, ConfiguredTaxError.YearMismatch); }
        if (!ValidSourceStructure(report) || report.YearPolicyVersion != ReportSnapshot.YearPolicyVersion
            || report.RoundingPolicyVersion != ReportSnapshot.RoundingPolicyVersion
            || report.CalculationVersion != FifoRealizedGainCalculator.HoldingsVersion)
        { return new(null, ConfiguredTaxError.InvalidSource); }
        try
        {
            var amounts = ConfiguredTaxCalculator.Calculate(Amount(report.Totals.ProfitUah),
                settings.InvestmentIncomePercent, settings.MilitaryPercent);
            return new(new(Guid.CreateVersion7(), report.PortfolioId, report.TaxYear, owner, captured,
                "UserConfigured", false, SchemaVersion, ConfiguredTaxCalculator.Version,
                ConfiguredTaxCalculator.RoundingPolicy, "PortfolioStockEtfSalesOnly", "NotIncluded",
                TaxSettingsDetails.From(settings), ReportSnapshot.Hash(ReportSnapshot.Serialize(report)), report, amounts));
        }
        catch (FormatException) { return new(null, ConfiguredTaxError.InvalidSource); }
        catch (OverflowException) { return new(null, ConfiguredTaxError.ArithmeticNotRepresentable); }
    }

    public static ConfiguredTaxResult<ConfiguredTaxDocument> Read(ConfiguredTaxReport saved, Guid owner, Guid portfolio)
    {
        if (saved.SchemaVersion != SchemaVersion || Encoding.UTF8.GetByteCount(saved.SnapshotJson) > MaxBytes
            || ReportSnapshot.Hash(saved.SnapshotJson) != saved.SnapshotSha256)
        { return new(null, ConfiguredTaxError.InvalidSnapshot); }
        try
        {
            var doc = JsonSerializer.Deserialize<ConfiguredTaxDocument>(saved.SnapshotJson, Options);
            if (doc is null || doc.Id != saved.Id || doc.PortfolioId != portfolio || doc.CreatedByAccountId != owner
                || doc.TaxYear != saved.TaxYear || doc.CreatedAtUtc != saved.CreatedAtUtc || doc.SchemaVersion != SchemaVersion
                || doc.Status != "UserConfigured" || doc.IsTaxReady || doc.CalculationVersion != ConfiguredTaxCalculator.Version
                || doc.RoundingPolicyVersion != ConfiguredTaxCalculator.RoundingPolicy
                || doc.IncomeScope != "PortfolioStockEtfSalesOnly" || doc.DividendStatus != "NotIncluded"
                || doc.Settings is null || doc.Settings.Id != saved.SettingsId || doc.Settings.TaxYear != doc.TaxYear
                || !ValidSettings(new(doc.Settings.InvestmentIncomePercent, doc.Settings.MilitaryPercent, doc.Settings.DividendIncomePercent))
                || doc.SourceReport is null || doc.SourceReport.Id != saved.SourceReportId
                || doc.SourceReport.PortfolioId != portfolio || doc.SourceReport.TaxYear != doc.TaxYear
                || !ValidSourceStructure(doc.SourceReport) || doc.SourceReport.Status != "Draft"
                || doc.SourceReport.IsTaxReady || doc.SourceReport.SchemaVersion != ReportSnapshot.SchemaVersion
                || doc.SourceReport.CalculationVersion != FifoRealizedGainCalculator.HoldingsVersion
                || doc.SourceReport.YearPolicyVersion != ReportSnapshot.YearPolicyVersion
                || doc.SourceReport.RoundingPolicyVersion != ReportSnapshot.RoundingPolicyVersion
                || ReportSnapshot.Hash(ReportSnapshot.Serialize(doc.SourceReport)) != doc.SourceSnapshotSha256
                || ReportSnapshot.InputHash(doc.SourceReport.Inputs) != doc.SourceReport.InputSha256
                || doc.Amounts != ConfiguredTaxCalculator.Calculate(Amount(doc.SourceReport.Totals.ProfitUah),
                    Amount(doc.Settings.InvestmentIncomePercent), Amount(doc.Settings.MilitaryPercent)))
            { return new(null, ConfiguredTaxError.InvalidSnapshot); }
            return new(doc);
        }
        catch (Exception error) when (error is JsonException or FormatException or OverflowException or ArgumentException)
        { return new(null, ConfiguredTaxError.InvalidSnapshot); }
    }

    public static byte[] Csv(ConfiguredTaxDocument document)
    {
        string[] headings = ["Status", "IsTaxReady", "TaxYear", "PortfolioId", "PortfolioName", "ReportId",
            "SourceReportId", "SettingsId", "NetProfitUah", "LossUah", "TaxBaseUah", "InvestmentIncomePercent",
            "MilitaryPercent", "DividendIncomePercentNotApplied", "InvestmentTaxUah", "MilitaryTaxUah", "TotalTaxUah",
            "IncomeScope", "DividendStatus", "CalculationVersion", "RoundingPolicyVersion"];
        string[] values = [document.Status, "false", document.TaxYear.ToString(CultureInfo.InvariantCulture),
            document.PortfolioId.ToString(), "'" + document.SourceReport.Inputs.Portfolio.Name, document.Id.ToString(),
            document.SourceReport.Id.ToString(), document.Settings.Id.ToString(), document.Amounts.NetProfitUah,
            document.Amounts.LossUah, document.Amounts.TaxBaseUah, document.Settings.InvestmentIncomePercent,
            document.Settings.MilitaryPercent, document.Settings.DividendIncomePercent, document.Amounts.InvestmentTaxUah,
            document.Amounts.MilitaryTaxUah, document.Amounts.TotalTaxUah, document.IncomeScope, document.DividendStatus,
            document.CalculationVersion, document.RoundingPolicyVersion];
        static string Line(IEnumerable<string> cells) => string.Join(",", cells.Select(c => "\"" + c.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + "\r\n";
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Line(headings) + Line(values))).ToArray();
    }

    private static bool ValidSourceStructure(TaxReportDocument report) => report.Totals is not null
        && report.Matches is not null && report.Inputs is not null && report.Inputs.Portfolio is not null
        && report.Inputs.Portfolio.Id == report.PortfolioId && report.Inputs.Portfolio.BaseCurrencyCode == "USD"
        && report.Inputs.Portfolio.Name is not null && report.Inputs.Trades is not null
        && report.Inputs.Instruments is not null && report.Inputs.Rates is not null && report.Inputs.Splits is not null;
}

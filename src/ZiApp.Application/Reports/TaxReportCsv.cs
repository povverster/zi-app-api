using System.Globalization;
using System.Text;

namespace ZiApp.Application.Reports;

public static class TaxReportCsv
{
    public static byte[] Export(TaxReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new StringBuilder();
        WriteRow(builder, ["row_type", "report_id", "portfolio_id", "portfolio_name", "tax_year", "status", "is_tax_ready",
            "calculation_version", "year_policy", "rounding_policy", "input_sha256", "instrument_id", "symbol", "exchange",
            "purchase_trade_id", "sale_trade_id", "purchase_broker_date", "sale_broker_date", "purchase_rate_id", "sale_rate_id",
            "quantity", "purchase_cost_usd", "purchase_cost_uah", "sale_proceeds_usd", "sale_proceeds_uah",
            "purchase_fee_usd", "purchase_fee_uah", "sale_fee_usd", "sale_fee_uah", "gross_difference_usd",
            "gross_difference_uah", "expenses_usd", "expenses_uah", "profit_usd", "profit_uah"]);
        string[] common = [document.Id.ToString("D"), document.PortfolioId.ToString("D"), SafeText(document.Inputs.Portfolio.Name),
            document.TaxYear.ToString(CultureInfo.InvariantCulture), "Draft", "false", document.CalculationVersion,
            document.YearPolicyVersion, document.RoundingPolicyVersion, document.InputSha256];
        var instruments = document.Inputs.Instruments.ToDictionary(i => i.Id);
        var trades = document.Inputs.Trades.ToDictionary(t => t.Id);
        foreach (var match in document.Matches)
        {
            var value = match.Values;
            var instrument = instruments[match.InstrumentId];
            WriteRow(builder, ["Match", .. common, instrument.Id.ToString("D"), SafeText(instrument.Symbol), SafeText(instrument.ExchangeCode),
                value.PurchaseTradeId.ToString("D"), value.SaleTradeId.ToString("D"),
                trades[value.PurchaseTradeId].BrokerDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                trades[value.SaleTradeId].BrokerDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                value.PurchaseRateId.ToString("D"), value.SaleRateId.ToString("D"), value.Quantity, value.PurchaseCostUsd,
                value.PurchaseCostUah, value.SaleProceedsUsd, value.SaleProceedsUah, value.PurchaseFeeUsd, value.PurchaseFeeUah,
                value.SaleFeeUsd, value.SaleFeeUah, match.Totals.GrossDifferenceUsd, match.Totals.GrossDifferenceUah,
                match.Totals.ExpensesUsd, match.Totals.ExpensesUah, match.Totals.ProfitUsd, match.Totals.ProfitUah]);
        }
        var total = document.Totals;
        WriteRow(builder, ["Total", .. common, .. Enumerable.Repeat("", 18),
            total.GrossDifferenceUsd, total.GrossDifferenceUah, total.ExpensesUsd, total.ExpensesUah, total.ProfitUsd, total.ProfitUah]);
        // UTF-8 BOM helps spreadsheet applications recognize non-ASCII portfolio names.
        return [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    // Quoting alone does not stop spreadsheet formula execution. Prefix ALL user text,
    // including text starting with whitespace/control characters, with a literal apostrophe.
    private static string SafeText(string value) => "'" + value;
    private static void WriteRow(StringBuilder builder, IEnumerable<string> cells) =>
        builder.AppendJoin(",", cells.Select(cell => "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")).Append("\r\n");
}

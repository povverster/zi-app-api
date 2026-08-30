using ZiApp.Domain.Accounts;
using ZiApp.Domain.Tax;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;

namespace ZiApp.UnitTests.InvestmentLedger;

public sealed class InvestmentLedgerEntityTests
{
    [Fact]
    public void UserAccountNormalizesEmailAndStartsActive()
    {
        var account = new UserAccount(
            Guid.CreateVersion7(),
            "  Investor@Example.com ",
            "Investor",
            AccountRole.User,
            SupportedLanguage.Ukrainian,
            DateTimeOffset.UtcNow);

        Assert.Equal("Investor@Example.com", account.Email);
        Assert.Equal("INVESTOR@EXAMPLE.COM", account.NormalizedEmail);
        Assert.True(account.IsActive);
    }

    [Fact]
    public void InvestmentTransactionRejectsNegativeFee()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvestmentTransaction(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            TradeSide.Buy,
            DateTimeOffset.UtcNow,
            1m,
            100m,
            -0.01m));
    }

    [Fact]
    public void TaxLotMatchSnapshotCopiesVersionedCalculationResults()
    {
        var calculation = new RealizedTaxLotMatch(
            "buy-1",
            "sell-1",
            2m,
            200m,
            8000m,
            220m,
            9020m,
            2m,
            80m,
            1m,
            41m);

        var snapshot = new TaxLotMatchSnapshot(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            calculation);

        Assert.Equal(17m, snapshot.ProfitUsd);
        Assert.Equal(899m, snapshot.ProfitUah);
    }
}
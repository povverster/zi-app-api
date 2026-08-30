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
            Guid.NewGuid(),
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
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
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
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            calculation);

        Assert.Equal(17m, snapshot.ProfitUsd);
        Assert.Equal(899m, snapshot.ProfitUah);
    }
}
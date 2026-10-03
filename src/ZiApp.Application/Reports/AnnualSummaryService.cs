using System.Text;

using ZiApp.Application.Security;
using ZiApp.Application.Trading;

namespace ZiApp.Application.Reports;

public sealed class AnnualSummaryService(ICurrentAccount account, IAnnualSummaryRepository repository)
{
    public async Task<AnnualResult<AnnualSummaryDocument>> CreateAsync(CreateAnnualSummaryInput input, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, AnnualSummaryError.AccountUnavailable); }
        if (!AnnualSummaryBuilder.ValidInput(input)) { return new(null, AnnualSummaryError.InvalidInput); }
        return await repository.CreateAsync(owner.Value, input, cancellationToken);
    }

    public async Task<AnnualResult<TradingPage<AnnualSummaryListItem>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, AnnualSummaryError.AccountUnavailable); }
        if (!TradingValidation.IsPageValid(page, pageSize)) { return new(null, AnnualSummaryError.InvalidInput); }
        return new(await repository.ListAsync(owner.Value, page, pageSize, cancellationToken));
    }

    public async Task<AnnualResult<AnnualSummaryDocument>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, AnnualSummaryError.AccountUnavailable); }
        var draft = await repository.FindAsync(owner.Value, id, cancellationToken);
        return draft is null ? new(null, AnnualSummaryError.NotFound) : AnnualSummarySnapshot.Read(draft);
    }

    public async Task<AnnualResult<ReportDownload>> ExportAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        if (owner is null) { return new(null, AnnualSummaryError.AccountUnavailable); }
        var draft = await repository.FindAsync(owner.Value, id, cancellationToken);
        if (draft is null) { return new(null, AnnualSummaryError.NotFound); }
        var saved = AnnualSummarySnapshot.Read(draft);
        if (saved.Value is null) { return new(null, saved.Error); }
        return new(new(Encoding.UTF8.GetBytes(draft.SnapshotJson), "application/json",
            $"ziapp-annual-preparation-{draft.TaxYear}-{id:N}.json"));
    }

    public async Task<AnnualResult<AnnualSummaryCurrentStatus>> CurrentStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        Guid? owner = await account.GetActiveAccountIdAsync(cancellationToken);
        return owner is null ? new(null, AnnualSummaryError.AccountUnavailable)
            : await repository.CurrentStatusAsync(owner.Value, id, cancellationToken);
    }
}

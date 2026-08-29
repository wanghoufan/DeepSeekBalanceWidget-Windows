using System.Threading;
using System.Threading.Tasks;
using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

public interface IOpenCodeUsageProvider
{
    Task<OpenCodeUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken);
}

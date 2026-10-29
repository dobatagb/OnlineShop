using Microsoft.Extensions.Logging;
using Shop.Application.Abstractions;

namespace Shop.Application.Common;

internal static class ConcurrencyRetry
{
    public const int MaxAttempts = 3;

    public static async Task<T> ExecuteAsync<T>(IUnitOfWork unitOfWork, ILogger logger, Func<Task<T>> operation)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogWarning("Concurrency conflict on attempt {Attempt}; retrying", attempt);
                unitOfWork.DiscardChanges();
            }
        }
    }
}

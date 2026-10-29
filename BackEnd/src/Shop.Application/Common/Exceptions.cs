namespace Shop.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class RequestValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ConcurrencyException(string message, Exception? innerException = null) : Exception(message, innerException);

namespace Shop.Domain.Common;

public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class DomainConflictException(string code, string message) : DomainException(code, message);

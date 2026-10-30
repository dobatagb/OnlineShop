using Shop.Application.Abstractions;
using Shop.Domain.Users;

namespace Shop.Application.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, string Email, string DisplayName);

public interface ILoginUseCase
{
    Task<LoginResponse?> ExecuteAsync(LoginRequest request, CancellationToken ct);
}

public sealed class LoginUseCase(IUserRepository users, IPasswordHasher passwordHasher, ITokenService tokens) : ILoginUseCase
{
    public async Task<LoginResponse?> ExecuteAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return null;
        }

        User? user = await users.GetByEmailAsync(User.NormalizeEmail(request.Email), ct);
        if (user is null || !passwordHasher.Verify(user, request.Password))
        {
            return null;
        }

        AccessToken token = tokens.CreateToken(user);
        return new LoginResponse(token.Token, "Bearer", token.ExpiresAt, user.Email, user.DisplayName);
    }
}

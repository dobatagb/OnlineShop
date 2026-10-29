namespace Shop.Domain.Users;

public class User
{
    private User() { }

    public User(string email, string displayName, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Email = NormalizeEmail(email);
        DisplayName = displayName;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

// Real file: Gringotts.Infrastructure/Auth/PasswordHasher.cs
// Three independent layers, stacked deliberately: BCrypt's own per-user salt, a shared
// server-side pepper (config only, never in the DB), and an adaptive work factor.

using Gringotts.Application.Common.Interfaces;
using Gringotts.Domain.Settings;
using Microsoft.Extensions.Options;

namespace Gringotts.Infrastructure.Auth;

public class PasswordHasher(IOptions<SecuritySettings> options) : IPasswordHasher
{
    private const int WorkFactor = 12;
    private readonly SecuritySettings _securitySettings = options.Value;

    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(AddPepper(password), WorkFactor);
    }

    public bool Verify(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(AddPepper(password), hash);
    }

    private string AddPepper(string password)
    {
        var pepperedPassword = password + _securitySettings.PasswordPepper;
        return pepperedPassword;
    }
}

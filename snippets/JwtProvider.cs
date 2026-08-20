// Real file: Gringotts.Infrastructure/Auth/JwtProvider.cs
// HMAC-SHA256 (HS256) signing under a dedicated secret key, issuer/audience/lifetime all
// validated on the receiving end (see Program.cs TokenValidationParameters). No secret
// values live here — they're pulled from configuration, never hardcoded.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Gringotts.Application.Common.Interfaces;
using Gringotts.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace Gringotts.Infrastructure.Auth;

public class JwtProvider(IConfiguration configuration) : IJwtProvider
{
    public string Generate(User user)
    {
        var claims = new Claim[]
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!)
        );

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            configuration["Jwt:Issuer"],
            configuration["Jwt:Audience"],
            claims,
            expires: DateTime.UtcNow.AddDays(
                int.Parse(configuration["Jwt:ExpirationInDays"]!)
            ),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;

        public AuthService(AppDbContext db, IConfiguration config, ILogger<AuthService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username, ct);
            if (user is null) return null;

            if (!VerifyPassword(request.Password, user.PasswordHash)) return null;

            var token = GenerateToken(user.Id, user.Username, user.Role);
            var expiresIn = long.TryParse(_config["Jwt:ExpireHours"], out var hours) ? hours * 3600 : 7 * 24 * 3600;

            return new LoginResponse
            {
                Token = token,
                ExpiresIn = expiresIn,
                User = ToDto(user)
            };
        }

        public async Task<LoginResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return null;
            }

            var exists = await _db.Users.AnyAsync(u => u.Username == request.Username, ct);
            if (exists) return null;

            // 首个注册用户自动成为管理员，便于初始化后台
            var isFirstUser = !await _db.Users.AnyAsync(ct);

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                Phone = request.Phone,
                PasswordHash = HashPassword(request.Password),
                Role = isFirstUser ? "admin" : "user",
                AvatarUrl = $"https://api.dicebear.com/7.x/initials/svg?seed={Uri.EscapeDataString(request.Username)}",
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("新用户注册: {Username}（角色 {Role}）", user.Username, user.Role);

            var token = GenerateToken(user.Id, user.Username, user.Role);
            return new LoginResponse
            {
                Token = token,
                ExpiresIn = 7 * 24 * 3600,
                User = ToDto(user)
            };
        }

        public async Task<UserDto?> GetProfileAsync(string userId, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return null;
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
            return user is null ? null : ToDto(user);
        }

        public string GenerateToken(long userId, string username, string role)
        {
            var key = _config["Jwt:Key"] ?? "ShortDrama-Aggregation-Platform-Default-Secret-Key-2025";
            var issuer = _config["Jwt:Issuer"] ?? "ShortDrama";
            var audience = _config["Jwt:Audience"] ?? "ShortDramaClient";
            var expireHours = double.TryParse(_config["Jwt:ExpireHours"], out var h) ? h : 168;

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(expireHours),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static UserDto ToDto(User u) => new()
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            Phone = u.Phone,
            AvatarUrl = u.AvatarUrl,
            Role = u.Role
        };

        /// <summary>PBKDF2 加盐哈希</summary>
        public static string HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
            return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool VerifyPassword(string password, string stored)
        {
            var parts = stored.Split('.');
            if (parts.Length != 2) return false;

            try
            {
                var salt = Convert.FromBase64String(parts[0]);
                var expected = Convert.FromBase64String(parts[1]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch
            {
                return false;
            }
        }
    }
}

using Microsoft.AspNetCore.Identity;

namespace KBCQuizManager.Web.Data.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Admin;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public Guid? CreatedById { get; set; }
    
    /// <summary>
    /// Unique code for admins that public users use to access their games/questions.
    /// Auto-generated when admin is created. Format: 6-char alphanumeric (e.g., "ADM4K7")
    /// </summary>
    public string? AdminCode { get; set; }
    
    /// <summary>
    /// For Player role: The admin this player is linked to via admin code during registration.
    /// </summary>
    public Guid? LinkedAdminId { get; set; }
    
    /// <summary>
    /// Email verification token for player registration
    /// </summary>
    public string? EmailVerificationToken { get; set; }
    
    /// <summary>
    /// Token expiry time
    /// </summary>
    public DateTime? EmailVerificationTokenExpiry { get; set; }
    
    // Navigation properties
    public virtual ApplicationUser? CreatedBy { get; set; }
    public virtual ApplicationUser? LinkedAdmin { get; set; }
    public virtual ICollection<Category> Categories { get; set; } = new List<Category>();
    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    public virtual ICollection<PublicUser> PublicUsers { get; set; } = new List<PublicUser>();
    public virtual ICollection<ApplicationUser> LinkedPlayers { get; set; } = new List<ApplicationUser>();
    
    public string FullName => $"{FirstName} {LastName}";
    
    /// <summary>
    /// Generate a unique 6-character admin code
    /// </summary>
    public static string GenerateAdminCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // No I,O,0,1 to avoid confusion
        var random = new Random();
        return new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }
    
    /// <summary>
    /// Generate a random email verification token
    /// </summary>
    public static string GenerateVerificationToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")
            .Substring(0, 20);
    }
}

public enum UserRole
{
    SuperAdmin = 1,
    Admin = 2,
    Player = 3
}

/// <summary>
/// A public user who registers via an admin's code to play games (anonymous/unregistered)
/// </summary>
public class PublicUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public Guid AdminId { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public int GamesPlayed { get; set; } = 0;
    public int TotalScore { get; set; } = 0;
    
    // Navigation
    public virtual ApplicationUser Admin { get; set; } = null!;
}

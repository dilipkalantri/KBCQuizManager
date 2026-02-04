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
    
    // Navigation properties
    public virtual ApplicationUser? CreatedBy { get; set; }
    public virtual ICollection<Category> Categories { get; set; } = new List<Category>();
    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    
    public string FullName => $"{FirstName} {LastName}";
}

public enum UserRole
{
    SuperAdmin = 1,
    Admin = 2
}

using System.ComponentModel.DataAnnotations;

namespace KBCQuizManager.Web.Data.Entities;

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }
    
    [MaxLength(50)]
    public string? IconName { get; set; } // MudBlazor icon name
    
    [MaxLength(20)]
    public string? Color { get; set; } // Hex color for theming
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Multi-tenant: Each category belongs to an admin
    public Guid OwnerId { get; set; }
    public virtual ApplicationUser Owner { get; set; } = null!;
    
    // Navigation
    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
    
    public int QuestionCount => Questions?.Count ?? 0;
}

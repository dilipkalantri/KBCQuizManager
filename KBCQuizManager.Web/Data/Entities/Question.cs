using System.ComponentModel.DataAnnotations;

namespace KBCQuizManager.Web.Data.Entities;

public class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(1000)]
    public string QuestionText { get; set; } = string.Empty;
    
    // KBC Style: 4 options (A, B, C, D)
    [Required]
    [MaxLength(500)]
    public string OptionA { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(500)]
    public string OptionB { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(500)]
    public string OptionC { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(500)]
    public string OptionD { get; set; } = string.Empty;
    
    // Correct answer (A, B, C, or D)
    [Required]
    public CorrectOption CorrectAnswer { get; set; }
    
    // KBC Style: Difficulty levels with prize amounts
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Easy;
    
    // Prize amount for this question (KBC style)
    public long PrizeAmount { get; set; } = 1000;
    
    // Optional hint/explanation
    [MaxLength(1000)]
    public string? Explanation { get; set; }
    
    // Time limit in seconds (default 60 for KBC)
    public int TimeLimitSeconds { get; set; } = 60;
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Relationships
    public Guid CategoryId { get; set; }
    public virtual Category Category { get; set; } = null!;
    
    // Multi-tenant: Each question belongs to an admin
    public Guid OwnerId { get; set; }
    public virtual ApplicationUser Owner { get; set; } = null!;
    
    public string GetCorrectAnswerText() => CorrectAnswer switch
    {
        CorrectOption.A => OptionA,
        CorrectOption.B => OptionB,
        CorrectOption.C => OptionC,
        CorrectOption.D => OptionD,
        _ => string.Empty
    };
}

public enum CorrectOption
{
    A = 1,
    B = 2,
    C = 3,
    D = 4
}

public enum DifficultyLevel
{
    Easy = 1,      // Questions 1-5 (₹1,000 - ₹10,000)
    Medium = 2,    // Questions 6-10 (₹20,000 - ₹3,20,000)
    Hard = 3,      // Questions 11-13 (₹6,40,000 - ₹25,00,000)
    Expert = 4     // Questions 14-15 (₹50,00,000 - ₹7 Crore)
}

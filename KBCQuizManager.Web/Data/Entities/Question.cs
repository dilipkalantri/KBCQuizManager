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
    
    // KBC Level (1-15) - determines prize amount
    [Range(1, 15)]
    public int Level { get; set; } = 1;
    
    // Optional hint/explanation (used for Phone a Friend lifeline)
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
    
    // Get prize amount based on level (KBC India prize structure)
    public long GetPrizeAmount() => Level switch
    {
        1 => 1000,
        2 => 2000,
        3 => 3000,
        4 => 5000,
        5 => 10000,        // First milestone
        6 => 20000,
        7 => 40000,
        8 => 80000,
        9 => 160000,
        10 => 320000,      // Second milestone
        11 => 640000,
        12 => 1250000,
        13 => 2500000,
        14 => 5000000,
        15 => 70000000,    // 7 Crore
        _ => 1000
    };
    
    // Get time limit based on level
    public int GetTimeLimitForLevel() => Level switch
    {
        <= 5 => 30,    // Easy questions: 30 seconds
        <= 10 => 45,   // Medium questions: 45 seconds
        <= 13 => 60,   // Hard questions: 60 seconds
        _ => 90        // Expert questions: 90 seconds
    };
}

public enum CorrectOption
{
    A = 1,
    B = 2,
    C = 3,
    D = 4
}

// KBC Prize Milestones
public static class KBCPrizeStructure
{
    public static readonly Dictionary<int, long> LevelPrizes = new()
    {
        { 1, 1000 },
        { 2, 2000 },
        { 3, 3000 },
        { 4, 5000 },
        { 5, 10000 },      // First guaranteed amount
        { 6, 20000 },
        { 7, 40000 },
        { 8, 80000 },
        { 9, 160000 },
        { 10, 320000 },    // Second guaranteed amount
        { 11, 640000 },
        { 12, 1250000 },
        { 13, 2500000 },
        { 14, 5000000 },
        { 15, 70000000 }   // 7 Crore
    };
    
    public static readonly int[] MilestoneLevels = { 5, 10 };
    
    public static long GetGuaranteedAmount(int currentLevel)
    {
        if (currentLevel >= 10) return LevelPrizes[10];
        if (currentLevel >= 5) return LevelPrizes[5];
        return 0;
    }
    
    public static string FormatPrize(long amount)
    {
        if (amount >= 10000000) return $"₹{amount / 10000000} Crore";
        if (amount >= 100000) return $"₹{amount / 100000} Lakh";
        return $"₹{amount:N0}";
    }
}

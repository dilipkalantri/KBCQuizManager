using System.ComponentModel.DataAnnotations;

namespace KBCQuizManager.Web.Data.Entities;

public class MultiplayerGame
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(10)]
    public string RoomCode { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string GameName { get; set; } = string.Empty;
    
    // Host (the admin who created the game)
    public Guid HostId { get; set; }
    public virtual ApplicationUser Host { get; set; } = null!;
    
    // Game settings
    public int MaxPlayers { get; set; } = 50;
    public int TimePerQuestion { get; set; } = 30; // seconds
    public int TotalQuestions { get; set; } = 15;
    
    // Game state
    public MultiplayerGameStatus Status { get; set; } = MultiplayerGameStatus.Waiting;
    public int CurrentQuestionIndex { get; set; } = 0;
    public Guid? CurrentQuestionId { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? QuestionStartedAt { get; set; }
    
    // Navigation
    public virtual ICollection<MultiplayerPlayer> Players { get; set; } = new List<MultiplayerPlayer>();
    public virtual ICollection<MultiplayerAnswer> Answers { get; set; } = new List<MultiplayerAnswer>();
    
    // Helper to generate room code
    public static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
    }
}

public class MultiplayerPlayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid GameId { get; set; }
    public virtual MultiplayerGame Game { get; set; } = null!;
    
    [Required]
    [MaxLength(50)]
    public string PlayerName { get; set; } = string.Empty;
    
    // Connection tracking
    public string? ConnectionId { get; set; }
    public bool IsConnected { get; set; } = true;
    
    // Score tracking
    public int TotalPoints { get; set; } = 0;
    public int CorrectAnswers { get; set; } = 0;
    public int WrongAnswers { get; set; } = 0;
    public int TotalTimeTaken { get; set; } = 0; // in milliseconds
    
    // Lifelines (each player gets their own)
    public bool FiftyFiftyUsed { get; set; } = false;
    public bool SkipQuestionUsed { get; set; } = false;
    public bool DoubleDipUsed { get; set; } = false;
    
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    
    // Current question state
    public bool HasAnsweredCurrent { get; set; } = false;
}

public class MultiplayerAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid GameId { get; set; }
    public virtual MultiplayerGame Game { get; set; } = null!;
    
    public Guid PlayerId { get; set; }
    public virtual MultiplayerPlayer Player { get; set; } = null!;
    
    public Guid QuestionId { get; set; }
    public virtual Question Question { get; set; } = null!;
    
    public int QuestionIndex { get; set; }
    
    // Answer details
    public CorrectOption? SelectedAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public int TimeTaken { get; set; } // in milliseconds
    public int PointsEarned { get; set; }
    
    // Lifelines used on this question
    public bool UsedFiftyFifty { get; set; }
    public bool UsedSkipQuestion { get; set; }
    public bool UsedDoubleDip { get; set; }
    public CorrectOption? DoubleDipFirstAnswer { get; set; }
    
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
}

public enum MultiplayerGameStatus
{
    Waiting = 1,      // Waiting for players to join
    Starting = 2,     // Game is about to start
    ShowingQuestion = 3, // Question is displayed, players can answer
    WaitingForAnswers = 4, // Timer ended, waiting for reveal
    ShowingAnswer = 5,    // Revealing correct answer
    ShowingLeaderboard = 6, // Between questions leaderboard
    Finished = 7      // Game completed
}

// Point calculation helper
public static class MultiplayerPoints
{
    // Base points per question level (1-15)
    public static readonly Dictionary<int, int> LevelPoints = new()
    {
        { 1, 100 },    { 2, 200 },    { 3, 300 },    { 4, 500 },    { 5, 1000 },
        { 6, 2000 },   { 7, 4000 },   { 8, 8000 },   { 9, 16000 },  { 10, 32000 },
        { 11, 64000 }, { 12, 125000 }, { 13, 250000 }, { 14, 500000 }, { 15, 1000000 }
    };
    
    public static int CalculatePoints(int questionLevel, int timeTakenMs, int maxTimeMs, bool isCorrect, bool usedLifeline)
    {
        if (!isCorrect)
        {
            // Negative points for wrong answer (25% of base points)
            return -(LevelPoints.GetValueOrDefault(questionLevel, 100) / 4);
        }
        
        int basePoints = LevelPoints.GetValueOrDefault(questionLevel, 100);
        
        // Time bonus: up to 50% extra for quick answers
        double timeRatio = 1.0 - ((double)timeTakenMs / maxTimeMs);
        int timeBonus = (int)(basePoints * 0.5 * Math.Max(0, timeRatio));
        
        // Lifeline penalty: 30% reduction
        int lifelinePenalty = usedLifeline ? (int)(basePoints * 0.3) : 0;
        
        return basePoints + timeBonus - lifelinePenalty;
    }
}

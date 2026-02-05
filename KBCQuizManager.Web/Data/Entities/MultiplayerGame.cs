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
    
    public Guid HostId { get; set; }
    public virtual ApplicationUser Host { get; set; } = null!;
    
    public int MaxPlayers { get; set; } = 50;
    public int TimePerQuestion { get; set; } = 30;
    public int TotalQuestions { get; set; } = 15;
    
    public MultiplayerGameStatus Status { get; set; } = MultiplayerGameStatus.Waiting;
    public int CurrentQuestionIndex { get; set; } = 0;
    public Guid? CurrentQuestionId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? QuestionStartedAt { get; set; }
    
    public virtual ICollection<MultiplayerPlayer> Players { get; set; } = new List<MultiplayerPlayer>();
    public virtual ICollection<MultiplayerAnswer> Answers { get; set; } = new List<MultiplayerAnswer>();
    
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
    
    public string? ConnectionId { get; set; }
    public bool IsConnected { get; set; } = true;
    public bool IsHost { get; set; } = false;
    
    public int TotalPoints { get; set; } = 0;
    public int CorrectAnswers { get; set; } = 0;
    public int WrongAnswers { get; set; } = 0;
    public int TotalTimeTaken { get; set; } = 0;
    
    public bool FiftyFiftyUsed { get; set; } = false;
    public bool SkipQuestionUsed { get; set; } = false;
    public bool DoubleDipUsed { get; set; } = false;
    
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
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
    
    public CorrectOption? SelectedAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public int TimeTaken { get; set; }
    public int PointsEarned { get; set; }
    
    public bool UsedFiftyFifty { get; set; }
    public bool UsedSkipQuestion { get; set; }
    public bool UsedDoubleDip { get; set; }
    public CorrectOption? DoubleDipFirstAnswer { get; set; }
    
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
}

public enum MultiplayerGameStatus
{
    Waiting = 1,
    Starting = 2,
    ShowingQuestion = 3,
    WaitingForAnswers = 4,
    ShowingAnswer = 5,
    ShowingLeaderboard = 6,
    Finished = 7
}

public static class MultiplayerPoints
{
    public static readonly Dictionary<int, int> LevelPoints = new()
    {
        { 1, 100 }, { 2, 200 }, { 3, 300 }, { 4, 500 }, { 5, 1000 },
        { 6, 2000 }, { 7, 4000 }, { 8, 8000 }, { 9, 16000 }, { 10, 32000 },
        { 11, 64000 }, { 12, 125000 }, { 13, 250000 }, { 14, 500000 }, { 15, 1000000 }
    };
    
    public static int CalculatePoints(int questionLevel, int timeTakenMs, int maxTimeMs, bool isCorrect, bool usedLifeline)
    {
        if (!isCorrect) return -(LevelPoints.GetValueOrDefault(questionLevel, 100) / 4);
        
        int basePoints = LevelPoints.GetValueOrDefault(questionLevel, 100);
        double timeRatio = 1.0 - ((double)timeTakenMs / maxTimeMs);
        int timeBonus = (int)(basePoints * 0.5 * Math.Max(0, timeRatio));
        int lifelinePenalty = usedLifeline ? (int)(basePoints * 0.3) : 0;
        
        return basePoints + timeBonus - lifelinePenalty;
    }
}

using System.ComponentModel.DataAnnotations;

namespace KBCQuizManager.Web.Data.Entities;

public class GameSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(100)]
    public string PlayerName { get; set; } = string.Empty;
    
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    
    // Game progress
    public int CurrentLevel { get; set; } = 1;
    public int QuestionsAnswered { get; set; } = 0;
    public int CorrectAnswers { get; set; } = 0;
    
    // Prize tracking
    public long CurrentPrize { get; set; } = 0;
    public long FinalPrize { get; set; } = 0;
    
    // Lifelines (true = used)
    public bool FiftyFiftyUsed { get; set; } = false;
    public bool PhoneAFriendUsed { get; set; } = false;
    public bool AudiencePollUsed { get; set; } = false;
    public bool ExpertAdviceUsed { get; set; } = false;
    
    // Game status
    public GameStatus Status { get; set; } = GameStatus.InProgress;
    
    // How the game ended
    public GameEndReason? EndReason { get; set; }
    
    // Total time taken in seconds
    public int TotalTimeTaken { get; set; } = 0;
    
    // Owner (admin who created the game)
    public Guid OwnerId { get; set; }
    public virtual ApplicationUser Owner { get; set; } = null!;
    
    // Game answers
    public virtual ICollection<GameSessionAnswer> Answers { get; set; } = new List<GameSessionAnswer>();
    
    // Helper methods
    public int GetRemainingLifelines()
    {
        int count = 0;
        if (!FiftyFiftyUsed) count++;
        if (!PhoneAFriendUsed) count++;
        if (!AudiencePollUsed) count++;
        if (!ExpertAdviceUsed) count++;
        return count;
    }
    
    public string GetDuration()
    {
        var duration = (EndedAt ?? DateTime.UtcNow) - StartedAt;
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1)
            return $"{duration.Minutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }
}

public class GameSessionAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid GameSessionId { get; set; }
    public virtual GameSession GameSession { get; set; } = null!;
    
    public Guid QuestionId { get; set; }
    public virtual Question Question { get; set; } = null!;
    
    // Level at which this question was asked
    public int Level { get; set; }
    
    // Player's answer (null if time ran out)
    public CorrectOption? PlayerAnswer { get; set; }
    
    // Was it correct?
    public bool IsCorrect { get; set; }
    
    // Time taken to answer (in seconds)
    public int TimeTaken { get; set; }
    
    // Did time run out?
    public bool TimedOut { get; set; } = false;
    
    // Lifelines used on this question
    public bool UsedFiftyFifty { get; set; } = false;
    public bool UsedPhoneAFriend { get; set; } = false;
    public bool UsedAudiencePoll { get; set; } = false;
    public bool UsedExpertAdvice { get; set; } = false;
    
    // Prize won at this level (0 if wrong)
    public long PrizeWon { get; set; }
    
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
}

public enum GameStatus
{
    InProgress = 1,
    Won = 2,           // Completed all 15 levels
    Lost = 3,          // Wrong answer
    Quit = 4,          // Player chose to quit
    TimedOut = 5       // Time ran out
}

public enum GameEndReason
{
    WrongAnswer = 1,
    TimedOut = 2,
    PlayerQuit = 3,
    CompletedAllLevels = 4
}

// Lifeline types
public enum LifelineType
{
    FiftyFifty = 1,      // Removes 2 wrong answers
    PhoneAFriend = 2,    // Shows hint/explanation
    AudiencePoll = 3,    // Shows percentage distribution
    ExpertAdvice = 4     // Shows expert's suggested answer
}

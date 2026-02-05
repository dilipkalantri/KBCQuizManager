using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface IGameService
{
    Task<GameSession> StartNewGameAsync(string playerName, Guid ownerId, Guid? playerId = null);
    Task<Question?> GetQuestionForLevelAsync(int level, Guid ownerId, List<Guid> usedQuestionIds);
    Task<GameSession?> GetGameSessionAsync(Guid sessionId);
    Task<List<GameSession>> GetGameHistoryAsync(Guid ownerId, int count = 20);
    Task<GameSession> RecordAnswerAsync(Guid sessionId, Guid questionId, CorrectOption? answer, int timeTaken, bool timedOut, LifelineUsage? lifelineUsed);
    Task<GameSession> UseLifelineAsync(Guid sessionId, LifelineType lifeline);
    Task<GameSession> QuitGameAsync(Guid sessionId);
    Task<GameSession> EndGameAsync(Guid sessionId, GameEndReason reason);
    Task<(List<CorrectOption> removedOptions, CorrectOption correctAnswer)> GetFiftyFiftyOptionsAsync(Question question);
    Task<AudiencePollResult> GetAudiencePollAsync(Question question);
    Task<string> GetPhoneAFriendHintAsync(Question question);
    Task<(CorrectOption suggestedAnswer, int confidence)> GetExpertAdviceAsync(Question question);
    Task<GameStatistics> GetGameStatisticsAsync(Guid ownerId);
    Task<List<GameSession>> GetPlayerGameHistoryAsync(Guid playerId, int count = 20);
    Task<GameStatistics> GetPlayerGameStatisticsAsync(Guid playerId);
    Task<List<LeaderboardEntry>> GetLeaderboardAsync(Guid adminId, int count = 20);
}

public class GameService : IGameService
{
    private readonly ApplicationDbContext _context;
    private readonly Random _random = new();

    public GameService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GameSession> StartNewGameAsync(string playerName, Guid ownerId, Guid? playerId = null)
    {
        // Clear any existing tracked entities
        _context.ChangeTracker.Clear();
        
        var session = new GameSession
        {
            PlayerName = playerName,
            OwnerId = ownerId,
            PlayerId = playerId,
            Status = GameStatus.InProgress,
            CurrentLevel = 1
        };

        _context.GameSessions.Add(session);
        await _context.SaveChangesAsync();
        
        // Detach to prevent tracking issues
        _context.Entry(session).State = EntityState.Detached;
        
        return session;
    }

    public async Task<Question?> GetQuestionForLevelAsync(int level, Guid ownerId, List<Guid> usedQuestionIds)
    {
        var query = _context.Questions
            .AsNoTracking()
            .Where(q => q.OwnerId == ownerId && q.IsActive && q.Level == level);

        if (usedQuestionIds.Any())
        {
            query = query.Where(q => !usedQuestionIds.Contains(q.Id));
        }

        var questions = await query.ToListAsync();
        if (!questions.Any()) return null;

        return questions[_random.Next(questions.Count)];
    }

    public async Task<GameSession?> GetGameSessionAsync(Guid sessionId)
    {
        return await _context.GameSessions
            .Include(g => g.Answers)
            .ThenInclude(a => a.Question)
            .FirstOrDefaultAsync(g => g.Id == sessionId);
    }

    public async Task<List<GameSession>> GetGameHistoryAsync(Guid ownerId, int count = 20)
    {
        return await _context.GameSessions
            .Where(g => g.OwnerId == ownerId)
            .OrderByDescending(g => g.StartedAt)
            .Take(count)
            .Include(g => g.Answers)
            .ToListAsync();
    }

    public async Task<GameSession> RecordAnswerAsync(Guid sessionId, Guid questionId, CorrectOption? answer, int timeTaken, bool timedOut, LifelineUsage? lifelineUsed)
    {
        // Clear change tracker to avoid concurrency issues
        _context.ChangeTracker.Clear();
        
        // Reload session fresh from database (without Include to avoid tracking issues)
        var session = await _context.GameSessions.FirstOrDefaultAsync(g => g.Id == sessionId);
        
        if (session == null) throw new Exception("Game session not found");

        var question = await _context.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == questionId);
        if (question == null) throw new Exception("Question not found");

        var isCorrect = !timedOut && answer.HasValue && answer.Value == question.CorrectAnswer;
        var prizeWon = isCorrect ? question.GetPrizeAmount() : 0;

        // Create the answer as a separate entity
        var sessionAnswer = new GameSessionAnswer
        {
            Id = Guid.NewGuid(),
            GameSessionId = sessionId,
            QuestionId = questionId,
            Level = session.CurrentLevel,
            PlayerAnswer = answer,
            IsCorrect = isCorrect,
            TimeTaken = timeTaken,
            TimedOut = timedOut,
            PrizeWon = prizeWon,
            AnsweredAt = DateTime.UtcNow,
            UsedFiftyFifty = lifelineUsed?.FiftyFifty ?? false,
            UsedPhoneAFriend = lifelineUsed?.PhoneAFriend ?? false,
            UsedAudiencePoll = lifelineUsed?.AudiencePoll ?? false,
            UsedExpertAdvice = lifelineUsed?.ExpertAdvice ?? false
        };

        // Add answer directly to the DbSet instead of through navigation property
        _context.GameSessionAnswers.Add(sessionAnswer);
        
        // Update session
        session.QuestionsAnswered++;
        session.TotalTimeTaken += timeTaken;

        if (isCorrect)
        {
            session.CorrectAnswers++;
            session.CurrentPrize = prizeWon;
            
            if (session.CurrentLevel >= 15)
            {
                // Won the game!
                session.Status = GameStatus.Won;
                session.FinalPrize = prizeWon;
                session.EndedAt = DateTime.UtcNow;
                session.EndReason = GameEndReason.CompletedAllLevels;
            }
            else
            {
                session.CurrentLevel++;
            }
        }
        else
        {
            // Wrong answer or timed out
            session.Status = timedOut ? GameStatus.TimedOut : GameStatus.Lost;
            session.FinalPrize = KBCPrizeStructure.GetGuaranteedAmount(session.CurrentLevel - 1);
            session.EndedAt = DateTime.UtcNow;
            session.EndReason = timedOut ? GameEndReason.TimedOut : GameEndReason.WrongAnswer;
        }

        await _context.SaveChangesAsync();
        return session;
    }

    public async Task<GameSession> UseLifelineAsync(Guid sessionId, LifelineType lifeline)
    {
        // Clear change tracker to avoid concurrency issues
        _context.ChangeTracker.Clear();
        
        var session = await _context.GameSessions.FirstOrDefaultAsync(g => g.Id == sessionId);
        if (session == null) throw new Exception("Game session not found");

        switch (lifeline)
        {
            case LifelineType.FiftyFifty:
                session.FiftyFiftyUsed = true;
                break;
            case LifelineType.PhoneAFriend:
                session.PhoneAFriendUsed = true;
                break;
            case LifelineType.AudiencePoll:
                session.AudiencePollUsed = true;
                break;
            case LifelineType.ExpertAdvice:
                session.ExpertAdviceUsed = true;
                break;
        }

        await _context.SaveChangesAsync();
        return session;
    }

    public async Task<GameSession> QuitGameAsync(Guid sessionId)
    {
        // Clear change tracker to avoid conflicts
        _context.ChangeTracker.Clear();
        
        var session = await _context.GameSessions.FirstOrDefaultAsync(g => g.Id == sessionId);
        if (session == null) throw new Exception("Game session not found");

        session.Status = GameStatus.Quit;
        session.FinalPrize = session.CurrentPrize;
        session.EndedAt = DateTime.UtcNow;
        session.EndReason = GameEndReason.PlayerQuit;

        await _context.SaveChangesAsync();
        return session;
    }

    public async Task<GameSession> EndGameAsync(Guid sessionId, GameEndReason reason)
    {
        var session = await _context.GameSessions.FindAsync(sessionId);
        if (session == null) throw new Exception("Game session not found");

        session.EndedAt = DateTime.UtcNow;
        session.EndReason = reason;
        session.Status = reason switch
        {
            GameEndReason.WrongAnswer => GameStatus.Lost,
            GameEndReason.TimedOut => GameStatus.TimedOut,
            GameEndReason.PlayerQuit => GameStatus.Quit,
            GameEndReason.CompletedAllLevels => GameStatus.Won,
            _ => GameStatus.Lost
        };

        if (reason != GameEndReason.CompletedAllLevels)
        {
            session.FinalPrize = KBCPrizeStructure.GetGuaranteedAmount(session.CurrentLevel - 1);
        }

        await _context.SaveChangesAsync();
        return session;
    }

    public Task<(List<CorrectOption> removedOptions, CorrectOption correctAnswer)> GetFiftyFiftyOptionsAsync(Question question)
    {
        var allOptions = new List<CorrectOption> { CorrectOption.A, CorrectOption.B, CorrectOption.C, CorrectOption.D };
        var wrongOptions = allOptions.Where(o => o != question.CorrectAnswer).ToList();
        
        // Remove 2 random wrong options
        var shuffled = wrongOptions.OrderBy(_ => _random.Next()).Take(2).ToList();
        
        return Task.FromResult((shuffled, question.CorrectAnswer));
    }

    public Task<AudiencePollResult> GetAudiencePollAsync(Question question)
    {
        // Simulate audience poll - correct answer gets highest percentage
        var correctPercent = _random.Next(45, 75);
        var remaining = 100 - correctPercent;
        
        var wrongOptions = new List<CorrectOption> { CorrectOption.A, CorrectOption.B, CorrectOption.C, CorrectOption.D }
            .Where(o => o != question.CorrectAnswer).ToList();
        
        var percentages = new Dictionary<CorrectOption, int>
        {
            { question.CorrectAnswer, correctPercent }
        };

        var remainingPercents = DistributePercentage(remaining, 3);
        for (int i = 0; i < wrongOptions.Count; i++)
        {
            percentages[wrongOptions[i]] = remainingPercents[i];
        }

        return Task.FromResult(new AudiencePollResult
        {
            PercentageA = percentages.GetValueOrDefault(CorrectOption.A),
            PercentageB = percentages.GetValueOrDefault(CorrectOption.B),
            PercentageC = percentages.GetValueOrDefault(CorrectOption.C),
            PercentageD = percentages.GetValueOrDefault(CorrectOption.D)
        });
    }

    public Task<string> GetPhoneAFriendHintAsync(Question question)
    {
        if (!string.IsNullOrEmpty(question.Explanation))
        {
            return Task.FromResult($"I think the answer might be related to: {question.Explanation}");
        }
        
        var hints = new[]
        {
            $"I'm pretty confident it's Option {question.CorrectAnswer}",
            $"I believe the answer is {question.CorrectAnswer}, but I'm not 100% sure",
            $"My best guess would be Option {question.CorrectAnswer}"
        };
        
        return Task.FromResult(hints[_random.Next(hints.Length)]);
    }

    public Task<(CorrectOption suggestedAnswer, int confidence)> GetExpertAdviceAsync(Question question)
    {
        // Expert is usually right (80-95% confidence)
        var confidence = _random.Next(80, 96);
        return Task.FromResult((question.CorrectAnswer, confidence));
    }

    public async Task<GameStatistics> GetGameStatisticsAsync(Guid ownerId)
    {
        var sessions = await _context.GameSessions
            .Where(g => g.OwnerId == ownerId)
            .ToListAsync();

        return new GameStatistics
        {
            TotalGames = sessions.Count,
            GamesWon = sessions.Count(s => s.Status == GameStatus.Won),
            GamesLost = sessions.Count(s => s.Status == GameStatus.Lost),
            GamesQuit = sessions.Count(s => s.Status == GameStatus.Quit),
            GamesTimedOut = sessions.Count(s => s.Status == GameStatus.TimedOut),
            TotalPrizeWon = sessions.Sum(s => s.FinalPrize),
            HighestPrize = sessions.Any() ? sessions.Max(s => s.FinalPrize) : 0,
            AverageLevel = sessions.Any() ? (int)sessions.Average(s => s.CurrentLevel) : 0,
            TotalQuestionsAnswered = sessions.Sum(s => s.QuestionsAnswered),
            TotalCorrectAnswers = sessions.Sum(s => s.CorrectAnswers)
        };
    }

    private int[] DistributePercentage(int total, int parts)
    {
        var result = new int[parts];
        var remaining = total;
        
        for (int i = 0; i < parts - 1; i++)
        {
            result[i] = _random.Next(0, remaining / (parts - i) + 1);
            remaining -= result[i];
        }
        result[parts - 1] = remaining;
        
        return result;
    }

    public async Task<List<GameSession>> GetPlayerGameHistoryAsync(Guid playerId, int count = 20)
    {
        return await _context.GameSessions
            .Where(g => g.PlayerId == playerId)
            .OrderByDescending(g => g.StartedAt)
            .Take(count)
            .Include(g => g.Answers)
            .ToListAsync();
    }

    public async Task<GameStatistics> GetPlayerGameStatisticsAsync(Guid playerId)
    {
        var sessions = await _context.GameSessions
            .Where(g => g.PlayerId == playerId)
            .ToListAsync();

        return new GameStatistics
        {
            TotalGames = sessions.Count,
            GamesWon = sessions.Count(s => s.Status == GameStatus.Won),
            GamesLost = sessions.Count(s => s.Status == GameStatus.Lost),
            GamesQuit = sessions.Count(s => s.Status == GameStatus.Quit),
            GamesTimedOut = sessions.Count(s => s.Status == GameStatus.TimedOut),
            TotalPrizeWon = sessions.Sum(s => s.FinalPrize),
            HighestPrize = sessions.Any() ? sessions.Max(s => s.FinalPrize) : 0,
            AverageLevel = sessions.Any() ? (int)sessions.Average(s => s.CurrentLevel) : 0,
            TotalQuestionsAnswered = sessions.Sum(s => s.QuestionsAnswered),
            TotalCorrectAnswers = sessions.Sum(s => s.CorrectAnswers)
        };
    }

    public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(Guid adminId, int count = 20)
    {
        // Get all game sessions for this admin's question bank, grouped by player
        var sessions = await _context.GameSessions
            .Where(g => g.OwnerId == adminId && g.PlayerId != null)
            .Include(g => g.Player)
            .ToListAsync();
        
        var leaderboard = sessions
            .Where(g => g.Player != null)
            .GroupBy(g => g.PlayerId!.Value)
            .Select(group => new LeaderboardEntry
            {
                PlayerId = group.Key,
                PlayerName = group.First().Player!.FullName,
                TotalGames = group.Count(),
                GamesWon = group.Count(g => g.Status == GameStatus.Won),
                HighestPrize = group.Max(g => g.FinalPrize),
                TotalPrize = group.Sum(g => g.FinalPrize),
                BestLevel = group.Max(g => g.CurrentLevel),
                TotalCorrectAnswers = group.Sum(g => g.CorrectAnswers)
            })
            .OrderByDescending(l => l.HighestPrize)
            .ThenByDescending(l => l.TotalPrize)
            .Take(count)
            .ToList();
        
        // Also include public user games (anonymous)
        var publicSessions = await _context.GameSessions
            .Where(g => g.OwnerId == adminId && g.PlayerId == null && g.PlayerName != null)
            .ToListAsync();
        
        var publicLeaderboard = publicSessions
            .GroupBy(g => g.PlayerName)
            .Select(group => new LeaderboardEntry
            {
                PlayerId = Guid.Empty,
                PlayerName = group.Key,
                TotalGames = group.Count(),
                GamesWon = group.Count(g => g.Status == GameStatus.Won),
                HighestPrize = group.Max(g => g.FinalPrize),
                TotalPrize = group.Sum(g => g.FinalPrize),
                BestLevel = group.Max(g => g.CurrentLevel),
                TotalCorrectAnswers = group.Sum(g => g.CorrectAnswers)
            });
        
        return leaderboard
            .Concat(publicLeaderboard)
            .OrderByDescending(l => l.HighestPrize)
            .ThenByDescending(l => l.TotalPrize)
            .Take(count)
            .ToList();
    }
}

public class LifelineUsage
{
    public bool FiftyFifty { get; set; }
    public bool PhoneAFriend { get; set; }
    public bool AudiencePoll { get; set; }
    public bool ExpertAdvice { get; set; }
}

public class AudiencePollResult
{
    public int PercentageA { get; set; }
    public int PercentageB { get; set; }
    public int PercentageC { get; set; }
    public int PercentageD { get; set; }
}

public class GameStatistics
{
    public int TotalGames { get; set; }
    public int GamesWon { get; set; }
    public int GamesLost { get; set; }
    public int GamesQuit { get; set; }
    public int GamesTimedOut { get; set; }
    public long TotalPrizeWon { get; set; }
    public long HighestPrize { get; set; }
    public int AverageLevel { get; set; }
    public int TotalQuestionsAnswered { get; set; }
    public int TotalCorrectAnswers { get; set; }
    public double AccuracyPercent => TotalQuestionsAnswered > 0 ? (double)TotalCorrectAnswers / TotalQuestionsAnswered * 100 : 0;
}

public class LeaderboardEntry
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TotalGames { get; set; }
    public int GamesWon { get; set; }
    public long HighestPrize { get; set; }
    public long TotalPrize { get; set; }
    public int BestLevel { get; set; }
    public int TotalCorrectAnswers { get; set; }
}

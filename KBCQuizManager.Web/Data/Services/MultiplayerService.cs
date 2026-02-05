using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface IMultiplayerService
{
    Task<MultiplayerGame> CreateGameAsync(Guid hostId, string gameName, int maxPlayers, int timePerQuestion);
    Task<MultiplayerGame?> GetGameByRoomCodeAsync(string roomCode);
    Task<MultiplayerGame?> GetGameByIdAsync(Guid gameId);
    Task<List<MultiplayerGame>> GetHostGamesAsync(Guid hostId);
    Task<List<MultiplayerPlayer>> GetPlayersAsync(Guid gameId);
    Task<List<LeaderboardEntry>> GetLeaderboardAsync(Guid gameId);
    Task DeleteGameAsync(Guid gameId);
    Task<bool> HasQuestionsForAllLevelsAsync(Guid hostId, int levels);
}

public class MultiplayerService : IMultiplayerService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<MultiplayerService> _logger;

    public MultiplayerService(ApplicationDbContext context, ILogger<MultiplayerService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<MultiplayerGame> CreateGameAsync(Guid hostId, string gameName, int maxPlayers, int timePerQuestion)
    {
        string roomCode;
        do { roomCode = MultiplayerGame.GenerateRoomCode(); }
        while (await _context.MultiplayerGames.AnyAsync(g => g.RoomCode == roomCode));

        var game = new MultiplayerGame
        {
            RoomCode = roomCode, GameName = gameName,
            HostId = hostId, MaxPlayers = maxPlayers,
            TimePerQuestion = timePerQuestion, TotalQuestions = 15
        };

        _context.MultiplayerGames.Add(game);
        await _context.SaveChangesAsync();
        return game;
    }

    public async Task<MultiplayerGame?> GetGameByRoomCodeAsync(string roomCode)
    {
        try
        {
            _context.ChangeTracker.Clear();
            return await _context.MultiplayerGames
                .Include(g => g.Players).Include(g => g.Host)
                .FirstOrDefaultAsync(g => g.RoomCode == roomCode.ToUpper());
        }
        catch (Exception ex) { _logger.LogError(ex, "Error getting game by room code"); return null; }
    }

    public async Task<MultiplayerGame?> GetGameByIdAsync(Guid gameId)
    {
        try
        {
            _context.ChangeTracker.Clear();
            return await _context.MultiplayerGames
                .Include(g => g.Players).Include(g => g.Host)
                .FirstOrDefaultAsync(g => g.Id == gameId);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error getting game by ID"); return null; }
    }

    public async Task<List<MultiplayerGame>> GetHostGamesAsync(Guid hostId)
    {
        try
        {
            return await _context.MultiplayerGames
                .Where(g => g.HostId == hostId)
                .OrderByDescending(g => g.CreatedAt)
                .Include(g => g.Players)
                .Take(20).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting host games");
            return new List<MultiplayerGame>();
        }
    }

    public async Task<List<MultiplayerPlayer>> GetPlayersAsync(Guid gameId)
    {
        try
        {
            return await _context.MultiplayerPlayers
                .Where(p => p.GameId == gameId)
                .OrderByDescending(p => p.TotalPoints).ThenBy(p => p.TotalTimeTaken)
                .ToListAsync();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error getting players"); return new(); }
    }

    public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(Guid gameId)
    {
        try
        {
            var players = await _context.MultiplayerPlayers
                .Where(p => p.GameId == gameId)
                .OrderByDescending(p => p.TotalPoints).ThenBy(p => p.TotalTimeTaken)
                .ToListAsync();

            return players.Select((p, index) => new LeaderboardEntry
            {
                Rank = index + 1,
                PlayerId = p.Id,
                PlayerName = p.PlayerName,
                TotalPoints = p.TotalPoints,
                CorrectAnswers = p.CorrectAnswers,
                WrongAnswers = p.WrongAnswers,
                TotalTimeTaken = p.TotalTimeTaken,
                AverageTime = (p.CorrectAnswers + p.WrongAnswers) > 0 
                    ? p.TotalTimeTaken / (p.CorrectAnswers + p.WrongAnswers) : 0,
                IsConnected = p.IsConnected,
                IsHost = p.IsHost
            }).ToList();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error getting leaderboard"); return new(); }
    }

    public async Task DeleteGameAsync(Guid gameId)
    {
        try
        {
            var game = await _context.MultiplayerGames.FindAsync(gameId);
            if (game != null) { _context.MultiplayerGames.Remove(game); await _context.SaveChangesAsync(); }
        }
        catch (Exception ex) { _logger.LogError(ex, "Error deleting game"); }
    }

    public async Task<bool> HasQuestionsForAllLevelsAsync(Guid hostId, int levels)
    {
        try
        {
            for (int i = 1; i <= levels; i++)
            {
                var count = await _context.Questions.CountAsync(q => q.OwnerId == hostId && q.IsActive && q.Level == i);
                if (count == 0) return false;
            }
            return true;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error checking questions"); return false; }
    }
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TotalPoints { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int TotalTimeTaken { get; set; }
    public int AverageTime { get; set; }
    public bool IsConnected { get; set; }
    public bool IsHost { get; set; }
}

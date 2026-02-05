using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Hubs;

public class MultiplayerHub : Hub
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<MultiplayerHub> _logger;

    public MultiplayerHub(ApplicationDbContext context, ILogger<MultiplayerHub> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Host joins a game room to receive updates
    public async Task JoinAsHost(string roomCode)
    {
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.RoomCode == roomCode.ToUpper());

        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Room not found");
            return;
        }

        // Add host to SignalR group
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        
        _logger.LogInformation("Host joined room {RoomCode}", roomCode);

        // Send current player list to host
        var playerList = game.Players.Select(p => new { p.Id, p.PlayerName, p.IsConnected, p.TotalPoints, p.HasAnsweredCurrent }).ToList();
        await Clients.Caller.SendAsync("PlayerListUpdated", playerList);
    }

    // Player joins a game room
    public async Task JoinRoom(string roomCode, string playerName)
    {
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.RoomCode == roomCode.ToUpper());

        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Room not found");
            return;
        }

        if (game.Status != MultiplayerGameStatus.Waiting)
        {
            await Clients.Caller.SendAsync("Error", "Game has already started");
            return;
        }

        if (game.Players.Count >= game.MaxPlayers)
        {
            await Clients.Caller.SendAsync("Error", "Room is full");
            return;
        }

        // Check if player name already exists
        if (game.Players.Any(p => p.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
        {
            await Clients.Caller.SendAsync("Error", "Player name already taken");
            return;
        }

        // Create player
        var player = new MultiplayerPlayer
        {
            GameId = game.Id,
            PlayerName = playerName,
            ConnectionId = Context.ConnectionId
        };

        _context.MultiplayerPlayers.Add(player);
        await _context.SaveChangesAsync();

        // Add to SignalR group
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        // Notify caller of success
        await Clients.Caller.SendAsync("JoinedRoom", new
        {
            PlayerId = player.Id,
            GameId = game.Id,
            GameName = game.GameName,
            RoomCode = game.RoomCode,
            PlayerName = playerName
        });

        // Notify all players in room
        var playerList = game.Players.Select(p => new { p.Id, p.PlayerName, p.IsConnected }).ToList();
        playerList.Add(new { player.Id, player.PlayerName, player.IsConnected });
        
        await Clients.Group(roomCode).SendAsync("PlayerListUpdated", playerList);
        await Clients.Group(roomCode).SendAsync("PlayerJoined", playerName);
    }

    // Host starts the game
    public async Task StartGame(Guid gameId)
    {
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null) return;

        if (game.Players.Count < 1)
        {
            await Clients.Caller.SendAsync("Error", "Need at least 1 player to start");
            return;
        }

        game.Status = MultiplayerGameStatus.Starting;
        game.StartedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await Clients.Group(game.RoomCode).SendAsync("GameStarting", 3); // 3 second countdown

        // Countdown handled on client, then host calls ShowNextQuestion
    }

    // Host shows next question
    public async Task ShowNextQuestion(Guid gameId)
    {
        _context.ChangeTracker.Clear();
        
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null) return;

        // Get random question for current level
        var questionLevel = game.CurrentQuestionIndex + 1;
        var usedQuestionIds = await _context.MultiplayerAnswers
            .Where(a => a.GameId == gameId)
            .Select(a => a.QuestionId)
            .Distinct()
            .ToListAsync();

        var question = await _context.Questions
            .AsNoTracking()
            .Where(q => q.OwnerId == game.HostId && q.IsActive && q.Level == questionLevel && !usedQuestionIds.Contains(q.Id))
            .OrderBy(q => Guid.NewGuid())
            .FirstOrDefaultAsync();

        if (question == null)
        {
            // Try to get any question at this level
            question = await _context.Questions
                .AsNoTracking()
                .Where(q => q.OwnerId == game.HostId && q.IsActive && q.Level == questionLevel)
                .OrderBy(q => Guid.NewGuid())
                .FirstOrDefaultAsync();
        }

        if (question == null)
        {
            await Clients.Caller.SendAsync("Error", $"No questions available for level {questionLevel}");
            return;
        }

        // Update game state
        game.CurrentQuestionId = question.Id;
        game.CurrentQuestionIndex = questionLevel;
        game.Status = MultiplayerGameStatus.ShowingQuestion;
        game.QuestionStartedAt = DateTime.UtcNow;
        
        // Reset all players' answered status
        foreach (var player in game.Players)
        {
            player.HasAnsweredCurrent = false;
        }
        
        await _context.SaveChangesAsync();

        // Send question to all players (without correct answer)
        await Clients.Group(game.RoomCode).SendAsync("QuestionRevealed", new
        {
            QuestionIndex = questionLevel,
            TotalQuestions = game.TotalQuestions,
            QuestionText = question.QuestionText,
            OptionA = question.OptionA,
            OptionB = question.OptionB,
            OptionC = question.OptionC,
            OptionD = question.OptionD,
            TimeLimit = game.TimePerQuestion,
            Points = MultiplayerPoints.LevelPoints.GetValueOrDefault(questionLevel, 100)
        });
    }

    // Player submits answer
    public async Task SubmitAnswer(Guid gameId, Guid playerId, string answer, int timeTakenMs, bool usedLifeline)
    {
        _context.ChangeTracker.Clear();
        
        _logger.LogInformation("SubmitAnswer called: gameId={GameId}, playerId={PlayerId}, answer={Answer}", gameId, playerId, answer);
        
        var game = await _context.MultiplayerGames.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) { _logger.LogWarning("SubmitAnswer: game not found"); return; }
        if (game.Status != MultiplayerGameStatus.ShowingQuestion) { _logger.LogWarning("SubmitAnswer: game status is {Status}, not ShowingQuestion", game.Status); return; }

        var player = await _context.MultiplayerPlayers.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null) { _logger.LogWarning("SubmitAnswer: player not found"); return; }
        if (player.HasAnsweredCurrent) { _logger.LogWarning("SubmitAnswer: player already answered"); return; }

        var question = await _context.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == game.CurrentQuestionId);
        if (question == null) { _logger.LogWarning("SubmitAnswer: question not found"); return; }

        CorrectOption? selectedOption = answer?.ToUpper() switch
        {
            "A" => CorrectOption.A,
            "B" => CorrectOption.B,
            "C" => CorrectOption.C,
            "D" => CorrectOption.D,
            _ => null
        };

        bool isCorrect = selectedOption.HasValue && selectedOption.Value == question.CorrectAnswer;
        _logger.LogInformation("SubmitAnswer: selected={Selected}, correct={Correct}, isCorrect={IsCorrect}", selectedOption, question.CorrectAnswer, isCorrect);
        
        int points = MultiplayerPoints.CalculatePoints(
            game.CurrentQuestionIndex,
            timeTakenMs,
            game.TimePerQuestion * 1000,
            isCorrect,
            usedLifeline
        );

        // Save answer
        var mpAnswer = new MultiplayerAnswer
        {
            GameId = gameId,
            PlayerId = playerId,
            QuestionId = question.Id,
            QuestionIndex = game.CurrentQuestionIndex,
            SelectedAnswer = selectedOption,
            IsCorrect = isCorrect,
            TimeTaken = timeTakenMs,
            PointsEarned = points,
            UsedFiftyFifty = usedLifeline
        };

        _context.MultiplayerAnswers.Add(mpAnswer);

        // Update player stats
        player.HasAnsweredCurrent = true;
        player.TotalPoints += points;
        player.TotalTimeTaken += timeTakenMs;
        if (isCorrect) player.CorrectAnswers++;
        else player.WrongAnswers++;

        await _context.SaveChangesAsync();

        // Notify host that player answered
        await Clients.Group(game.RoomCode).SendAsync("PlayerAnswered", new
        {
            PlayerId = playerId,
            PlayerName = player.PlayerName,
            HasAnswered = true
        });

        // Check if all players answered
        var allPlayers = await _context.MultiplayerPlayers.Where(p => p.GameId == gameId).ToListAsync();
        if (allPlayers.All(p => p.HasAnsweredCurrent))
        {
            await Clients.Group(game.RoomCode).SendAsync("AllPlayersAnswered");
        }
    }

    // Host reveals answer
    public async Task RevealAnswer(Guid gameId)
    {
        _context.ChangeTracker.Clear();
        
        var game = await _context.MultiplayerGames.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) return;

        var question = await _context.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == game.CurrentQuestionId);
        if (question == null) return;

        game.Status = MultiplayerGameStatus.ShowingAnswer;
        await _context.SaveChangesAsync();

        // Get all answers for this question
        var answers = await _context.MultiplayerAnswers
            .Include(a => a.Player)
            .Where(a => a.GameId == gameId && a.QuestionIndex == game.CurrentQuestionIndex)
            .ToListAsync();

        _logger.LogInformation("RevealAnswer: Found {Count} answers for question index {Index}", answers.Count, game.CurrentQuestionIndex);

        var answerStats = new
        {
            CorrectAnswer = question.CorrectAnswer.ToString(),
            Explanation = question.Explanation,
            PlayerResults = answers.Select(a => new
            {
                PlayerId = a.PlayerId,
                PlayerName = a.Player.PlayerName,
                SelectedAnswer = a.SelectedAnswer?.ToString(),
                IsCorrect = a.IsCorrect,
                PointsEarned = a.PointsEarned,
                TimeTaken = a.TimeTaken
            }).ToList()
        };

        await Clients.Group(game.RoomCode).SendAsync("AnswerRevealed", answerStats);
    }

    // Host shows leaderboard
    public async Task ShowLeaderboard(Guid gameId)
    {
        _context.ChangeTracker.Clear();
        
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null) return;

        game.Status = MultiplayerGameStatus.ShowingLeaderboard;
        await _context.SaveChangesAsync();

        var leaderboard = game.Players
            .OrderByDescending(p => p.TotalPoints)
            .ThenBy(p => p.TotalTimeTaken)
            .Select((p, index) => new
            {
                Rank = index + 1,
                p.Id,
                p.PlayerName,
                p.TotalPoints,
                p.CorrectAnswers,
                p.WrongAnswers,
                AvgTime = p.CorrectAnswers > 0 ? p.TotalTimeTaken / (p.CorrectAnswers + p.WrongAnswers) : 0
            })
            .ToList();

        await Clients.Group(game.RoomCode).SendAsync("LeaderboardUpdated", new
        {
            CurrentQuestion = game.CurrentQuestionIndex,
            TotalQuestions = game.TotalQuestions,
            Leaderboard = leaderboard
        });
    }

    // Host ends game
    public async Task EndGame(Guid gameId)
    {
        _context.ChangeTracker.Clear();
        
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null) return;

        game.Status = MultiplayerGameStatus.Finished;
        game.EndedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var finalLeaderboard = game.Players
            .OrderByDescending(p => p.TotalPoints)
            .ThenBy(p => p.TotalTimeTaken)
            .Select((p, index) => new
            {
                Rank = index + 1,
                p.Id,
                p.PlayerName,
                p.TotalPoints,
                p.CorrectAnswers,
                p.WrongAnswers
            })
            .ToList();

        await Clients.Group(game.RoomCode).SendAsync("GameEnded", new
        {
            Winner = finalLeaderboard.FirstOrDefault()?.PlayerName,
            FinalLeaderboard = finalLeaderboard
        });
    }

    // Player uses lifeline (50:50)
    public async Task UseFiftyFifty(Guid gameId, Guid playerId)
    {
        var player = await _context.MultiplayerPlayers.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null || player.FiftyFiftyUsed) return;

        var game = await _context.MultiplayerGames.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) return;

        var question = await _context.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == game.CurrentQuestionId);
        if (question == null) return;

        player.FiftyFiftyUsed = true;
        await _context.SaveChangesAsync();

        // Get two wrong options to hide
        var allOptions = new[] { CorrectOption.A, CorrectOption.B, CorrectOption.C, CorrectOption.D };
        var wrongOptions = allOptions.Where(o => o != question.CorrectAnswer).ToList();
        var random = new Random();
        var hiddenOptions = wrongOptions.OrderBy(_ => random.Next()).Take(2).ToList();

        await Clients.Caller.SendAsync("FiftyFiftyResult", hiddenOptions.Select(o => o.ToString()).ToList());
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Find player by connection ID and mark as disconnected
        var player = await _context.MultiplayerPlayers
            .Include(p => p.Game)
            .FirstOrDefaultAsync(p => p.ConnectionId == Context.ConnectionId);

        if (player != null)
        {
            player.IsConnected = false;
            player.ConnectionId = null;
            await _context.SaveChangesAsync();

            await Clients.Group(player.Game.RoomCode).SendAsync("PlayerDisconnected", player.PlayerName);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Reconnect player
    public async Task Reconnect(Guid playerId, string roomCode)
    {
        var player = await _context.MultiplayerPlayers
            .Include(p => p.Game)
            .FirstOrDefaultAsync(p => p.Id == playerId);

        if (player == null || player.Game.RoomCode != roomCode.ToUpper())
        {
            await Clients.Caller.SendAsync("Error", "Could not reconnect");
            return;
        }

        player.ConnectionId = Context.ConnectionId;
        player.IsConnected = true;
        await _context.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);
        
        await Clients.Caller.SendAsync("Reconnected", new
        {
            player.Game.Status,
            player.Game.CurrentQuestionIndex,
            player.TotalPoints
        });

        await Clients.Group(roomCode).SendAsync("PlayerReconnected", player.PlayerName);
    }
}

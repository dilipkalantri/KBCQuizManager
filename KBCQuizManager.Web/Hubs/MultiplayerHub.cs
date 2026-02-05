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

    // ===== HOST JOINS (also registers as player) =====
    public async Task JoinAsHost(string roomCode, string hostName)
    {
        _context.ChangeTracker.Clear();
        
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.RoomCode == roomCode.ToUpper());

        if (game == null)
        {
            await Clients.Caller.SendAsync("Error", "Room not found");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode.ToUpper());

        // Create host player if not exists
        var hostPlayer = game.Players.FirstOrDefault(p => p.IsHost);
        if (hostPlayer == null)
        {
            hostPlayer = new MultiplayerPlayer
            {
                GameId = game.Id,
                PlayerName = hostName + " ⭐",
                ConnectionId = Context.ConnectionId,
                IsHost = true
            };
            _context.MultiplayerPlayers.Add(hostPlayer);
            await _context.SaveChangesAsync();
        }
        else
        {
            hostPlayer.ConnectionId = Context.ConnectionId;
            hostPlayer.IsConnected = true;
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Host {Name} joined room {Room} as player {Id}", hostName, roomCode, hostPlayer.Id);

        // Send host their player ID
        await Clients.Caller.SendAsync("HostPlayerReady", hostPlayer.Id);

        // Broadcast updated player list
        _context.ChangeTracker.Clear();
        var refreshed = await _context.MultiplayerGames.Include(g => g.Players).FirstOrDefaultAsync(g => g.Id == game.Id);
        var playerList = refreshed!.Players.Select(p => new { p.Id, p.PlayerName, p.IsConnected, p.TotalPoints, p.HasAnsweredCurrent, p.IsHost }).ToList();
        await Clients.Group(roomCode.ToUpper()).SendAsync("PlayerListUpdated", playerList);
    }

    // ===== PLAYER JOINS =====
    public async Task JoinRoom(string roomCode, string playerName)
    {
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.RoomCode == roomCode.ToUpper());

        if (game == null) { await Clients.Caller.SendAsync("Error", "Room not found"); return; }
        if (game.Status != MultiplayerGameStatus.Waiting) { await Clients.Caller.SendAsync("Error", "Game has already started"); return; }
        if (game.Players.Count >= game.MaxPlayers) { await Clients.Caller.SendAsync("Error", "Room is full"); return; }
        if (game.Players.Any(p => p.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
        { await Clients.Caller.SendAsync("Error", "Player name already taken"); return; }

        var player = new MultiplayerPlayer
        {
            GameId = game.Id,
            PlayerName = playerName,
            ConnectionId = Context.ConnectionId
        };

        _context.MultiplayerPlayers.Add(player);
        await _context.SaveChangesAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode.ToUpper());

        await Clients.Caller.SendAsync("JoinedRoom", new
        {
            PlayerId = player.Id, GameId = game.Id,
            GameName = game.GameName, RoomCode = game.RoomCode, PlayerName = playerName
        });

        var playerList = game.Players.Select(p => new { p.Id, p.PlayerName, p.IsConnected, p.IsHost }).ToList();
        playerList.Add(new { player.Id, player.PlayerName, player.IsConnected, IsHost = false });

        await Clients.Group(roomCode.ToUpper()).SendAsync("PlayerListUpdated", playerList);
        await Clients.Group(roomCode.ToUpper()).SendAsync("PlayerJoined", playerName);
    }

    // ===== START GAME =====
    public async Task StartGame(Guid gameId)
    {
        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameId);

        if (game == null) return;

        var playerCount = game.Players.Count(p => !p.IsHost);
        if (playerCount < 1)
        {
            await Clients.Caller.SendAsync("Error", "Need at least 1 player to start");
            return;
        }

        game.Status = MultiplayerGameStatus.Starting;
        game.StartedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await Clients.Group(game.RoomCode).SendAsync("GameStarting", 3);
    }

    // ===== SHOW NEXT QUESTION =====
    public async Task ShowNextQuestion(Guid gameId)
    {
        _context.ChangeTracker.Clear();

        var game = await _context.MultiplayerGames
            .Include(g => g.Players)
            .FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) return;

        var questionLevel = game.CurrentQuestionIndex + 1;
        var usedQuestionIds = await _context.MultiplayerAnswers
            .Where(a => a.GameId == gameId)
            .Select(a => a.QuestionId)
            .Distinct()
            .ToListAsync();

        var question = await _context.Questions.AsNoTracking()
            .Where(q => q.OwnerId == game.HostId && q.IsActive && q.Level == questionLevel && !usedQuestionIds.Contains(q.Id))
            .OrderBy(q => Guid.NewGuid())
            .FirstOrDefaultAsync();

        if (question == null)
        {
            question = await _context.Questions.AsNoTracking()
                .Where(q => q.OwnerId == game.HostId && q.IsActive && q.Level == questionLevel)
                .OrderBy(q => Guid.NewGuid())
                .FirstOrDefaultAsync();
        }

        if (question == null)
        {
            await Clients.Caller.SendAsync("Error", $"No questions for level {questionLevel}");
            return;
        }

        game.CurrentQuestionId = question.Id;
        game.CurrentQuestionIndex = questionLevel;
        game.Status = MultiplayerGameStatus.ShowingQuestion;
        game.QuestionStartedAt = DateTime.UtcNow;

        foreach (var player in game.Players)
            player.HasAnsweredCurrent = false;

        await _context.SaveChangesAsync();

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

        // Broadcast initial live leaderboard
        await BroadcastLiveLeaderboard(gameId, game.RoomCode);
    }

    // ===== SUBMIT ANSWER =====
    public async Task SubmitAnswer(Guid gameId, Guid playerId, string answer, int timeTakenMs, bool usedLifeline)
    {
        _context.ChangeTracker.Clear();

        _logger.LogInformation("SubmitAnswer: game={G}, player={P}, answer={A}", gameId, playerId, answer);

        var game = await _context.MultiplayerGames.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) { _logger.LogWarning("SubmitAnswer: game not found"); return; }
        if (game.Status != MultiplayerGameStatus.ShowingQuestion) { _logger.LogWarning("SubmitAnswer: wrong status {S}", game.Status); return; }

        var player = await _context.MultiplayerPlayers.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null) { _logger.LogWarning("SubmitAnswer: player not found"); return; }
        if (player.HasAnsweredCurrent) { _logger.LogWarning("SubmitAnswer: already answered"); return; }

        var question = await _context.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == game.CurrentQuestionId);
        if (question == null) { _logger.LogWarning("SubmitAnswer: question not found"); return; }

        CorrectOption? selectedOption = answer?.ToUpper() switch
        {
            "A" => CorrectOption.A, "B" => CorrectOption.B,
            "C" => CorrectOption.C, "D" => CorrectOption.D, _ => null
        };

        bool isCorrect = selectedOption.HasValue && selectedOption.Value == question.CorrectAnswer;
        int points = MultiplayerPoints.CalculatePoints(game.CurrentQuestionIndex, timeTakenMs, game.TimePerQuestion * 1000, isCorrect, usedLifeline);
        _logger.LogInformation("SubmitAnswer: selected={S}, correct={C}, isCorrect={I}, pts={P}", selectedOption, question.CorrectAnswer, isCorrect, points);

        _context.MultiplayerAnswers.Add(new MultiplayerAnswer
        {
            GameId = gameId, PlayerId = playerId, QuestionId = question.Id,
            QuestionIndex = game.CurrentQuestionIndex, SelectedAnswer = selectedOption,
            IsCorrect = isCorrect, TimeTaken = timeTakenMs, PointsEarned = points,
            UsedFiftyFifty = usedLifeline
        });

        player.HasAnsweredCurrent = true;
        player.TotalPoints += points;
        player.TotalTimeTaken += timeTakenMs;
        if (isCorrect) player.CorrectAnswers++;
        else player.WrongAnswers++;

        await _context.SaveChangesAsync();

        await Clients.Group(game.RoomCode).SendAsync("PlayerAnswered", new
        {
            PlayerId = playerId, PlayerName = player.PlayerName, HasAnswered = true
        });

        // Broadcast live leaderboard
        await BroadcastLiveLeaderboard(gameId, game.RoomCode);

        // Check if all players answered
        _context.ChangeTracker.Clear();
        var allPlayers = await _context.MultiplayerPlayers.Where(p => p.GameId == gameId).ToListAsync();
        if (allPlayers.All(p => p.HasAnsweredCurrent))
        {
            await Clients.Group(game.RoomCode).SendAsync("AllPlayersAnswered");
        }
    }

    // ===== REVEAL ANSWER =====
    public async Task RevealAnswer(Guid gameId)
    {
        _context.ChangeTracker.Clear();

        var game = await _context.MultiplayerGames.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) return;

        var question = await _context.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == game.CurrentQuestionId);
        if (question == null) return;

        game.Status = MultiplayerGameStatus.ShowingAnswer;
        await _context.SaveChangesAsync();

        var answers = await _context.MultiplayerAnswers
            .Include(a => a.Player)
            .Where(a => a.GameId == gameId && a.QuestionIndex == game.CurrentQuestionIndex)
            .ToListAsync();

        await Clients.Group(game.RoomCode).SendAsync("AnswerRevealed", new
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
                TimeTaken = a.TimeTaken,
                UsedSkip = a.UsedSkipQuestion
            }).ToList()
        });
    }

    // ===== SHOW LEADERBOARD =====
    public async Task ShowLeaderboard(Guid gameId)
    {
        _context.ChangeTracker.Clear();

        var game = await _context.MultiplayerGames.Include(g => g.Players).FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) return;

        game.Status = MultiplayerGameStatus.ShowingLeaderboard;
        await _context.SaveChangesAsync();

        var leaderboard = game.Players
            .OrderByDescending(p => p.TotalPoints).ThenBy(p => p.TotalTimeTaken)
            .Select((p, i) => new
            {
                Rank = i + 1, p.Id, p.PlayerName, p.TotalPoints,
                p.CorrectAnswers, p.WrongAnswers, p.TotalTimeTaken, p.IsHost
            }).ToList();

        await Clients.Group(game.RoomCode).SendAsync("LeaderboardUpdated", new
        {
            CurrentQuestion = game.CurrentQuestionIndex,
            TotalQuestions = game.TotalQuestions,
            Leaderboard = leaderboard
        });
    }

    // ===== END GAME =====
    public async Task EndGame(Guid gameId)
    {
        _context.ChangeTracker.Clear();

        var game = await _context.MultiplayerGames.Include(g => g.Players).FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) return;

        game.Status = MultiplayerGameStatus.Finished;
        game.EndedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var finalLeaderboard = game.Players
            .OrderByDescending(p => p.TotalPoints).ThenBy(p => p.TotalTimeTaken)
            .Select((p, i) => new
            {
                Rank = i + 1, p.Id, p.PlayerName, p.TotalPoints,
                p.CorrectAnswers, p.WrongAnswers, p.TotalTimeTaken, p.IsHost
            }).ToList();

        await Clients.Group(game.RoomCode).SendAsync("GameEnded", new
        {
            Winner = finalLeaderboard.FirstOrDefault()?.PlayerName,
            FinalLeaderboard = finalLeaderboard
        });
    }

    // ===== LIFELINE: 50:50 =====
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

        var allOptions = new[] { CorrectOption.A, CorrectOption.B, CorrectOption.C, CorrectOption.D };
        var wrongOptions = allOptions.Where(o => o != question.CorrectAnswer).ToList();
        var hidden = wrongOptions.OrderBy(_ => Random.Shared.Next()).Take(2).Select(o => o.ToString()).ToList();

        await Clients.Caller.SendAsync("FiftyFiftyResult", hidden);
    }

    // ===== LIFELINE: SKIP QUESTION =====
    public async Task SkipQuestion(Guid gameId, Guid playerId)
    {
        _context.ChangeTracker.Clear();

        var player = await _context.MultiplayerPlayers.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null || player.SkipQuestionUsed || player.HasAnsweredCurrent) return;

        var game = await _context.MultiplayerGames.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null || game.Status != MultiplayerGameStatus.ShowingQuestion) return;

        player.SkipQuestionUsed = true;
        player.HasAnsweredCurrent = true;

        _context.MultiplayerAnswers.Add(new MultiplayerAnswer
        {
            GameId = gameId, PlayerId = playerId,
            QuestionId = game.CurrentQuestionId ?? Guid.Empty,
            QuestionIndex = game.CurrentQuestionIndex,
            SelectedAnswer = null, IsCorrect = false,
            TimeTaken = 0, PointsEarned = 0, UsedSkipQuestion = true
        });

        await _context.SaveChangesAsync();

        await Clients.Caller.SendAsync("QuestionSkipped");

        await Clients.Group(game.RoomCode).SendAsync("PlayerAnswered", new
        {
            PlayerId = playerId, PlayerName = player.PlayerName, HasAnswered = true
        });

        await BroadcastLiveLeaderboard(gameId, game.RoomCode);

        _context.ChangeTracker.Clear();
        var allPlayers = await _context.MultiplayerPlayers.Where(p => p.GameId == gameId).ToListAsync();
        if (allPlayers.All(p => p.HasAnsweredCurrent))
            await Clients.Group(game.RoomCode).SendAsync("AllPlayersAnswered");
    }

    // ===== LIFELINE: DOUBLE DIP (first attempt check) =====
    public async Task DoubleDipCheck(Guid gameId, Guid playerId, string answer)
    {
        _context.ChangeTracker.Clear();

        var player = await _context.MultiplayerPlayers.FirstOrDefaultAsync(p => p.Id == playerId);
        if (player == null || player.HasAnsweredCurrent) return;

        if (!player.DoubleDipUsed)
        {
            player.DoubleDipUsed = true;
            await _context.SaveChangesAsync();
        }

        var game = await _context.MultiplayerGames.FirstOrDefaultAsync(g => g.Id == gameId);
        if (game == null) return;

        var question = await _context.Questions.AsNoTracking().FirstOrDefaultAsync(q => q.Id == game.CurrentQuestionId);
        if (question == null) return;

        CorrectOption? selectedOption = answer?.ToUpper() switch
        {
            "A" => CorrectOption.A, "B" => CorrectOption.B,
            "C" => CorrectOption.C, "D" => CorrectOption.D, _ => null
        };

        bool isCorrect = selectedOption.HasValue && selectedOption.Value == question.CorrectAnswer;

        if (isCorrect)
        {
            // Correct on first try with double dip - record with penalty
            int timeTakenMs = (int)(DateTime.UtcNow - (game.QuestionStartedAt ?? DateTime.UtcNow)).TotalMilliseconds;
            int points = MultiplayerPoints.CalculatePoints(game.CurrentQuestionIndex, timeTakenMs, game.TimePerQuestion * 1000, true, true);

            _context.MultiplayerAnswers.Add(new MultiplayerAnswer
            {
                GameId = gameId, PlayerId = playerId, QuestionId = question.Id,
                QuestionIndex = game.CurrentQuestionIndex, SelectedAnswer = selectedOption,
                IsCorrect = true, TimeTaken = timeTakenMs, PointsEarned = points, UsedDoubleDip = true
            });

            player.HasAnsweredCurrent = true;
            player.TotalPoints += points;
            player.TotalTimeTaken += timeTakenMs;
            player.CorrectAnswers++;

            await _context.SaveChangesAsync();

            await Clients.Caller.SendAsync("DoubleDipResult", new { IsCorrect = true, Answer = answer, Points = points });

            await Clients.Group(game.RoomCode).SendAsync("PlayerAnswered", new
            {
                PlayerId = playerId, PlayerName = player.PlayerName, HasAnswered = true
            });
            await BroadcastLiveLeaderboard(gameId, game.RoomCode);

            _context.ChangeTracker.Clear();
            var allPlayers = await _context.MultiplayerPlayers.Where(p => p.GameId == gameId).ToListAsync();
            if (allPlayers.All(p => p.HasAnsweredCurrent))
                await Clients.Group(game.RoomCode).SendAsync("AllPlayersAnswered");
        }
        else
        {
            // Wrong first attempt - eliminate option, player gets another try
            await Clients.Caller.SendAsync("DoubleDipResult", new { IsCorrect = false, Answer = answer, Points = 0 });
        }
    }

    // ===== LIVE LEADERBOARD BROADCAST =====
    private async Task BroadcastLiveLeaderboard(Guid gameId, string roomCode)
    {
        _context.ChangeTracker.Clear();

        var players = await _context.MultiplayerPlayers
            .Where(p => p.GameId == gameId)
            .OrderByDescending(p => p.TotalPoints).ThenBy(p => p.TotalTimeTaken)
            .ToListAsync();

        var leaderboard = players.Select((p, i) => new
        {
            Rank = i + 1, p.Id, p.PlayerName, p.TotalPoints,
            p.CorrectAnswers, p.WrongAnswers, p.TotalTimeTaken, p.IsHost, p.HasAnsweredCurrent
        }).ToList();

        await Clients.Group(roomCode).SendAsync("LiveLeaderboard", leaderboard);
    }

    // ===== DISCONNECT =====
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
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

    // ===== RECONNECT =====
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

        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode.ToUpper());

        await Clients.Caller.SendAsync("Reconnected", new
        {
            player.Game.Status,
            player.Game.CurrentQuestionIndex,
            player.TotalPoints
        });

        await Clients.Group(roomCode.ToUpper()).SendAsync("PlayerReconnected", player.PlayerName);
    }
}

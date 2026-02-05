using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;
using System.Text.Json;

namespace KBCQuizManager.Web.Data.Services;

public interface IQuestionService
{
    Task<List<Question>> GetQuestionsAsync(Guid ownerId, Guid? categoryId = null, int? level = null);
    Task<List<Question>> GetAllQuestionsAsync();
    Task<Question?> GetQuestionByIdAsync(Guid id, Guid ownerId);
    Task<Question?> GetByIdAsync(Guid id);
    Task<(bool Success, string Message)> CreateQuestionAsync(Question question);
    Task<(bool Success, string Message)> UpdateQuestionAsync(Question question, Guid ownerId);
    Task<(bool Success, string Message)> DeleteQuestionAsync(Guid id, Guid ownerId);
    Task<(bool Success, string Message)> ToggleQuestionStatusAsync(Guid id, Guid ownerId);
    Task<(bool Success, string Message)> BulkDeleteQuestionsAsync(List<Guid> ids, Guid ownerId);
    Task<QuestionStatistics> GetStatisticsAsync(Guid ownerId);
    Task<List<Question>> GetRandomQuestionsForGameAsync(Guid ownerId, int count = 15);
    Task<Dictionary<int, int>> GetQuestionCountByLevelAsync(Guid ownerId);
    Task<(int Success, int Failed, string Message)> ImportQuestionsFromJsonAsync(string json, Guid categoryId, Guid ownerId);
}

public class QuestionService : IQuestionService
{
    private readonly ApplicationDbContext _context;
    
    public QuestionService(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Question>> GetQuestionsAsync(Guid ownerId, Guid? categoryId = null, int? level = null)
    {
        var query = _context.Questions
            .Where(q => q.OwnerId == ownerId)
            .Include(q => q.Category)
            .AsQueryable();
        
        if (categoryId.HasValue)
            query = query.Where(q => q.CategoryId == categoryId.Value);
            
        if (level.HasValue)
            query = query.Where(q => q.Level == level.Value);
        
        return await query
            .OrderBy(q => q.Level)
            .ThenByDescending(q => q.CreatedAt)
            .ToListAsync();
    }
    
    public async Task<List<Question>> GetAllQuestionsAsync()
    {
        return await _context.Questions
            .Include(q => q.Owner)
            .Include(q => q.Category)
            .OrderBy(q => q.Level)
            .ThenByDescending(q => q.CreatedAt)
            .ToListAsync();
    }
    
    public async Task<Question?> GetQuestionByIdAsync(Guid id, Guid ownerId)
    {
        return await _context.Questions
            .Include(q => q.Category)
            .FirstOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);
    }
    
    public async Task<Question?> GetByIdAsync(Guid id)
    {
        return await _context.Questions
            .AsNoTracking()
            .Include(q => q.Category)
            .FirstOrDefaultAsync(q => q.Id == id);
    }
    
    public async Task<(bool Success, string Message)> CreateQuestionAsync(Question question)
    {
        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == question.CategoryId && c.OwnerId == question.OwnerId);
            
        if (!categoryExists)
            return (false, "Invalid category selected");
        
        // Validate level
        if (question.Level < 1 || question.Level > 15)
            question.Level = 1;
        
        question.Id = Guid.NewGuid();
        question.CreatedAt = DateTime.UtcNow;
        question.TimeLimitSeconds = question.GetTimeLimitForLevel();
        
        _context.Questions.Add(question);
        await _context.SaveChangesAsync();
        
        return (true, "Question created successfully");
    }
    
    public async Task<(bool Success, string Message)> UpdateQuestionAsync(Question question, Guid ownerId)
    {
        var existingQuestion = await _context.Questions
            .FirstOrDefaultAsync(q => q.Id == question.Id && q.OwnerId == ownerId);
            
        if (existingQuestion == null)
            return (false, "Question not found");
        
        if (existingQuestion.CategoryId != question.CategoryId)
        {
            var categoryExists = await _context.Categories
                .AnyAsync(c => c.Id == question.CategoryId && c.OwnerId == ownerId);
                
            if (!categoryExists)
                return (false, "Invalid category selected");
        }
        
        existingQuestion.QuestionText = question.QuestionText;
        existingQuestion.OptionA = question.OptionA;
        existingQuestion.OptionB = question.OptionB;
        existingQuestion.OptionC = question.OptionC;
        existingQuestion.OptionD = question.OptionD;
        existingQuestion.CorrectAnswer = question.CorrectAnswer;
        existingQuestion.Level = Math.Clamp(question.Level, 1, 15);
        existingQuestion.Explanation = question.Explanation;
        existingQuestion.TimeLimitSeconds = existingQuestion.GetTimeLimitForLevel();
        existingQuestion.CategoryId = question.CategoryId;
        existingQuestion.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return (true, "Question updated successfully");
    }
    
    public async Task<(bool Success, string Message)> DeleteQuestionAsync(Guid id, Guid ownerId)
    {
        var question = await _context.Questions
            .FirstOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);
            
        if (question == null)
            return (false, "Question not found");
        
        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();
        
        return (true, "Question deleted successfully");
    }
    
    public async Task<(bool Success, string Message)> ToggleQuestionStatusAsync(Guid id, Guid ownerId)
    {
        var question = await _context.Questions
            .FirstOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);
            
        if (question == null)
            return (false, "Question not found");
        
        question.IsActive = !question.IsActive;
        question.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        
        var status = question.IsActive ? "activated" : "deactivated";
        return (true, $"Question {status} successfully");
    }
    
    public async Task<(bool Success, string Message)> BulkDeleteQuestionsAsync(List<Guid> ids, Guid ownerId)
    {
        var questions = await _context.Questions
            .Where(q => ids.Contains(q.Id) && q.OwnerId == ownerId)
            .ToListAsync();
        
        if (!questions.Any())
            return (false, "No questions found to delete");
        
        _context.Questions.RemoveRange(questions);
        await _context.SaveChangesAsync();
        
        return (true, $"{questions.Count} question(s) deleted successfully");
    }
    
    public async Task<QuestionStatistics> GetStatisticsAsync(Guid ownerId)
    {
        var questions = await _context.Questions
            .Where(q => q.OwnerId == ownerId)
            .ToListAsync();
        
        var categoryCount = await _context.Categories
            .CountAsync(c => c.OwnerId == ownerId);
        
        var levelCounts = questions.GroupBy(q => q.Level)
            .ToDictionary(g => g.Key, g => g.Count());
        
        return new QuestionStatistics
        {
            TotalQuestions = questions.Count,
            ActiveQuestions = questions.Count(q => q.IsActive),
            TotalCategories = categoryCount,
            LevelCounts = levelCounts,
            // For backward compatibility
            EasyQuestions = levelCounts.Where(kv => kv.Key <= 5).Sum(kv => kv.Value),
            MediumQuestions = levelCounts.Where(kv => kv.Key >= 6 && kv.Key <= 10).Sum(kv => kv.Value),
            HardQuestions = levelCounts.Where(kv => kv.Key >= 11 && kv.Key <= 13).Sum(kv => kv.Value),
            ExpertQuestions = levelCounts.Where(kv => kv.Key >= 14).Sum(kv => kv.Value)
        };
    }
    
    public async Task<Dictionary<int, int>> GetQuestionCountByLevelAsync(Guid ownerId)
    {
        var counts = await _context.Questions
            .Where(q => q.OwnerId == ownerId && q.IsActive)
            .GroupBy(q => q.Level)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync();
        
        // Initialize all 15 levels
        var result = Enumerable.Range(1, 15).ToDictionary(l => l, l => 0);
        foreach (var item in counts)
        {
            result[item.Level] = item.Count;
        }
        
        return result;
    }
    
    public async Task<List<Question>> GetRandomQuestionsForGameAsync(Guid ownerId, int count = 15)
    {
        var result = new List<Question>();
        
        // Get one question for each level
        for (int level = 1; level <= 15; level++)
        {
            var question = await _context.Questions
                .Where(q => q.OwnerId == ownerId && q.IsActive && q.Level == level)
                .OrderBy(q => Guid.NewGuid())
                .FirstOrDefaultAsync();
            
            if (question != null)
                result.Add(question);
        }
        
        return result;
    }
    
    public async Task<(int Success, int Failed, string Message)> ImportQuestionsFromJsonAsync(string json, Guid categoryId, Guid ownerId)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var importData = JsonSerializer.Deserialize<QuestionImportData>(json, options);
            if (importData?.Questions == null || !importData.Questions.Any())
                return (0, 0, "No questions found in JSON");
            
            var categoryExists = await _context.Categories
                .AnyAsync(c => c.Id == categoryId && c.OwnerId == ownerId);
            
            if (!categoryExists)
                return (0, 0, "Invalid category selected");
            
            int success = 0, failed = 0;
            
            foreach (var q in importData.Questions)
            {
                try
                {
                    var question = new Question
                    {
                        Id = Guid.NewGuid(),
                        QuestionText = q.Question ?? q.QuestionText ?? "",
                        OptionA = q.OptionA ?? q.Options?.A ?? "",
                        OptionB = q.OptionB ?? q.Options?.B ?? "",
                        OptionC = q.OptionC ?? q.Options?.C ?? "",
                        OptionD = q.OptionD ?? q.Options?.D ?? "",
                        CorrectAnswer = ParseAnswer(q.Answer ?? q.CorrectAnswer ?? "A"),
                        Level = Math.Clamp(q.Level, 1, 15),
                        Explanation = q.Explanation ?? q.Hint,
                        CategoryId = categoryId,
                        OwnerId = ownerId,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    };
                    
                    question.TimeLimitSeconds = question.GetTimeLimitForLevel();
                    
                    if (!string.IsNullOrWhiteSpace(question.QuestionText) &&
                        !string.IsNullOrWhiteSpace(question.OptionA) &&
                        !string.IsNullOrWhiteSpace(question.OptionB) &&
                        !string.IsNullOrWhiteSpace(question.OptionC) &&
                        !string.IsNullOrWhiteSpace(question.OptionD))
                    {
                        _context.Questions.Add(question);
                        success++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }
            
            await _context.SaveChangesAsync();
            return (success, failed, $"Imported {success} questions successfully" + (failed > 0 ? $", {failed} failed" : ""));
        }
        catch (JsonException ex)
        {
            return (0, 0, $"Invalid JSON format: {ex.Message}");
        }
    }
    
    private static CorrectOption ParseAnswer(string answer)
    {
        return answer.ToUpper().Trim() switch
        {
            "A" or "1" => CorrectOption.A,
            "B" or "2" => CorrectOption.B,
            "C" or "3" => CorrectOption.C,
            "D" or "4" => CorrectOption.D,
            _ => CorrectOption.A
        };
    }
}

// JSON Import Models
public class QuestionImportData
{
    public List<QuestionImportItem> Questions { get; set; } = new();
}

public class QuestionImportItem
{
    public string? Question { get; set; }
    public string? QuestionText { get; set; }
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
    public QuestionImportOptions? Options { get; set; }
    public string? Answer { get; set; }
    public string? CorrectAnswer { get; set; }
    public int Level { get; set; } = 1;
    public string? Explanation { get; set; }
    public string? Hint { get; set; }
}

public class QuestionImportOptions
{
    public string? A { get; set; }
    public string? B { get; set; }
    public string? C { get; set; }
    public string? D { get; set; }
}

public class QuestionStatistics
{
    public int TotalQuestions { get; set; }
    public int ActiveQuestions { get; set; }
    public int TotalCategories { get; set; }
    public Dictionary<int, int> LevelCounts { get; set; } = new();
    public int EasyQuestions { get; set; }
    public int MediumQuestions { get; set; }
    public int HardQuestions { get; set; }
    public int ExpertQuestions { get; set; }
}

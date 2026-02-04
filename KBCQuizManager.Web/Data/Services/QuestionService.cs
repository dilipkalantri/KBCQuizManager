using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface IQuestionService
{
    Task<List<Question>> GetQuestionsAsync(Guid ownerId, Guid? categoryId = null, DifficultyLevel? difficulty = null);
    Task<List<Question>> GetAllQuestionsAsync(); // For SuperAdmin
    Task<Question?> GetQuestionByIdAsync(Guid id, Guid ownerId);
    Task<(bool Success, string Message)> CreateQuestionAsync(Question question);
    Task<(bool Success, string Message)> UpdateQuestionAsync(Question question, Guid ownerId);
    Task<(bool Success, string Message)> DeleteQuestionAsync(Guid id, Guid ownerId);
    Task<(bool Success, string Message)> ToggleQuestionStatusAsync(Guid id, Guid ownerId);
    Task<(bool Success, string Message)> BulkDeleteQuestionsAsync(List<Guid> ids, Guid ownerId);
    Task<QuestionStatistics> GetStatisticsAsync(Guid ownerId);
    Task<List<Question>> GetRandomQuestionsForGameAsync(Guid ownerId, int count = 15);
}

public class QuestionService : IQuestionService
{
    private readonly ApplicationDbContext _context;
    
    public QuestionService(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Question>> GetQuestionsAsync(Guid ownerId, Guid? categoryId = null, DifficultyLevel? difficulty = null)
    {
        var query = _context.Questions
            .Where(q => q.OwnerId == ownerId)
            .Include(q => q.Category)
            .AsQueryable();
        
        if (categoryId.HasValue)
            query = query.Where(q => q.CategoryId == categoryId.Value);
            
        if (difficulty.HasValue)
            query = query.Where(q => q.Difficulty == difficulty.Value);
        
        return await query
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();
    }
    
    public async Task<List<Question>> GetAllQuestionsAsync()
    {
        return await _context.Questions
            .Include(q => q.Owner)
            .Include(q => q.Category)
            .OrderByDescending(q => q.CreatedAt)
            .ToListAsync();
    }
    
    public async Task<Question?> GetQuestionByIdAsync(Guid id, Guid ownerId)
    {
        return await _context.Questions
            .Include(q => q.Category)
            .FirstOrDefaultAsync(q => q.Id == id && q.OwnerId == ownerId);
    }
    
    public async Task<(bool Success, string Message)> CreateQuestionAsync(Question question)
    {
        // Verify category exists and belongs to owner
        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == question.CategoryId && c.OwnerId == question.OwnerId);
            
        if (!categoryExists)
            return (false, "Invalid category selected");
        
        question.Id = Guid.NewGuid();
        question.CreatedAt = DateTime.UtcNow;
        
        // Auto-set prize amount based on difficulty
        question.PrizeAmount = GetPrizeAmountForDifficulty(question.Difficulty);
        
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
        
        // Verify new category if changed
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
        existingQuestion.Difficulty = question.Difficulty;
        existingQuestion.PrizeAmount = GetPrizeAmountForDifficulty(question.Difficulty);
        existingQuestion.Explanation = question.Explanation;
        existingQuestion.TimeLimitSeconds = question.TimeLimitSeconds;
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
        
        return new QuestionStatistics
        {
            TotalQuestions = questions.Count,
            ActiveQuestions = questions.Count(q => q.IsActive),
            TotalCategories = categoryCount,
            EasyQuestions = questions.Count(q => q.Difficulty == DifficultyLevel.Easy),
            MediumQuestions = questions.Count(q => q.Difficulty == DifficultyLevel.Medium),
            HardQuestions = questions.Count(q => q.Difficulty == DifficultyLevel.Hard),
            ExpertQuestions = questions.Count(q => q.Difficulty == DifficultyLevel.Expert)
        };
    }
    
    public async Task<List<Question>> GetRandomQuestionsForGameAsync(Guid ownerId, int count = 15)
    {
        // Get questions distributed by difficulty (KBC style)
        var result = new List<Question>();
        
        // Easy: 5 questions
        var easyQuestions = await _context.Questions
            .Where(q => q.OwnerId == ownerId && q.IsActive && q.Difficulty == DifficultyLevel.Easy)
            .OrderBy(q => Guid.NewGuid())
            .Take(5)
            .ToListAsync();
        result.AddRange(easyQuestions);
        
        // Medium: 5 questions
        var mediumQuestions = await _context.Questions
            .Where(q => q.OwnerId == ownerId && q.IsActive && q.Difficulty == DifficultyLevel.Medium)
            .OrderBy(q => Guid.NewGuid())
            .Take(5)
            .ToListAsync();
        result.AddRange(mediumQuestions);
        
        // Hard: 3 questions
        var hardQuestions = await _context.Questions
            .Where(q => q.OwnerId == ownerId && q.IsActive && q.Difficulty == DifficultyLevel.Hard)
            .OrderBy(q => Guid.NewGuid())
            .Take(3)
            .ToListAsync();
        result.AddRange(hardQuestions);
        
        // Expert: 2 questions
        var expertQuestions = await _context.Questions
            .Where(q => q.OwnerId == ownerId && q.IsActive && q.Difficulty == DifficultyLevel.Expert)
            .OrderBy(q => Guid.NewGuid())
            .Take(2)
            .ToListAsync();
        result.AddRange(expertQuestions);
        
        return result;
    }
    
    private static long GetPrizeAmountForDifficulty(DifficultyLevel difficulty)
    {
        return difficulty switch
        {
            DifficultyLevel.Easy => 10000,      // ₹10,000
            DifficultyLevel.Medium => 320000,   // ₹3,20,000
            DifficultyLevel.Hard => 2500000,    // ₹25,00,000
            DifficultyLevel.Expert => 70000000, // ₹7 Crore
            _ => 1000
        };
    }
}

public class QuestionStatistics
{
    public int TotalQuestions { get; set; }
    public int ActiveQuestions { get; set; }
    public int TotalCategories { get; set; }
    public int EasyQuestions { get; set; }
    public int MediumQuestions { get; set; }
    public int HardQuestions { get; set; }
    public int ExpertQuestions { get; set; }
}

using Microsoft.EntityFrameworkCore;
using KBCQuizManager.Web.Data.Entities;

namespace KBCQuizManager.Web.Data.Services;

public interface ICategoryService
{
    Task<List<Category>> GetCategoriesAsync(Guid ownerId);
    Task<List<Category>> GetAllCategoriesAsync(); // For SuperAdmin
    Task<Category?> GetCategoryByIdAsync(Guid id, Guid ownerId);
    Task<(bool Success, string Message)> CreateCategoryAsync(Category category);
    Task<(bool Success, string Message, Category? Category)> CreateCategoryWithReturnAsync(Category category);
    Task<(bool Success, string Message)> UpdateCategoryAsync(Category category, Guid ownerId);
    Task<(bool Success, string Message)> DeleteCategoryAsync(Guid id, Guid ownerId);
    Task<(bool Success, string Message)> ToggleCategoryStatusAsync(Guid id, Guid ownerId);
    Task<bool> CategoryExistsAsync(string name, Guid ownerId, Guid? excludeId = null);
}

public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _context;
    
    public CategoryService(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Category>> GetCategoriesAsync(Guid ownerId)
    {
        return await _context.Categories
            .Where(c => c.OwnerId == ownerId)
            .Include(c => c.Questions)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }
    
    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        return await _context.Categories
            .Include(c => c.Owner)
            .Include(c => c.Questions)
            .OrderBy(c => c.Owner.FirstName)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }
    
    public async Task<Category?> GetCategoryByIdAsync(Guid id, Guid ownerId)
    {
        return await _context.Categories
            .Include(c => c.Questions)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);
    }
    
    public async Task<(bool Success, string Message)> CreateCategoryAsync(Category category)
    {
        // Check for duplicate name within the same owner
        var exists = await CategoryExistsAsync(category.Name, category.OwnerId);
        if (exists)
            return (false, "A category with this name already exists");
        
        category.Id = Guid.NewGuid();
        category.CreatedAt = DateTime.UtcNow;
        
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        
        return (true, "Category created successfully");
    }
    
    public async Task<(bool Success, string Message, Category? Category)> CreateCategoryWithReturnAsync(Category category)
    {
        // Check for duplicate name within the same owner
        var exists = await CategoryExistsAsync(category.Name, category.OwnerId);
        if (exists)
            return (false, "A category with this name already exists", null);
        
        category.Id = Guid.NewGuid();
        category.CreatedAt = DateTime.UtcNow;
        
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        
        return (true, "Category created successfully", category);
    }
    
    public async Task<(bool Success, string Message)> UpdateCategoryAsync(Category category, Guid ownerId)
    {
        var existingCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == category.Id && c.OwnerId == ownerId);
            
        if (existingCategory == null)
            return (false, "Category not found");
        
        // Check for duplicate name (excluding current category)
        var duplicateExists = await CategoryExistsAsync(category.Name, ownerId, category.Id);
        if (duplicateExists)
            return (false, "A category with this name already exists");
        
        existingCategory.Name = category.Name;
        existingCategory.Description = category.Description;
        existingCategory.IconName = category.IconName;
        existingCategory.Color = category.Color;
        existingCategory.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return (true, "Category updated successfully");
    }
    
    public async Task<(bool Success, string Message)> DeleteCategoryAsync(Guid id, Guid ownerId)
    {
        var category = await _context.Categories
            .Include(c => c.Questions)
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);
            
        if (category == null)
            return (false, "Category not found");
        
        if (category.Questions.Any())
            return (false, $"Cannot delete category with {category.Questions.Count} questions. Delete questions first or move them to another category.");
        
        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        
        return (true, "Category deleted successfully");
    }
    
    public async Task<(bool Success, string Message)> ToggleCategoryStatusAsync(Guid id, Guid ownerId)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId);
            
        if (category == null)
            return (false, "Category not found");
        
        category.IsActive = !category.IsActive;
        category.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        
        var status = category.IsActive ? "activated" : "deactivated";
        return (true, $"Category {status} successfully");
    }
    
    public async Task<bool> CategoryExistsAsync(string name, Guid ownerId, Guid? excludeId = null)
    {
        var query = _context.Categories
            .Where(c => c.OwnerId == ownerId && c.Name.ToLower() == name.ToLower());
            
        if (excludeId.HasValue)
            query = query.Where(c => c.Id != excludeId.Value);
            
        return await query.AnyAsync();
    }
}

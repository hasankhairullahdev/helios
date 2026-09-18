using Microsoft.EntityFrameworkCore;
using TaskService.Models;

namespace TaskService.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
}

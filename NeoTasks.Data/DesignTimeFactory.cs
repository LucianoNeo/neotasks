using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace NeoTasks;
public class DesignTimeFactory:IDesignTimeDbContextFactory<TasksDb>{public TasksDb CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<TasksDb>().UseNpgsql("Host=localhost;Database=design;Username=design;Password=design").Options);}

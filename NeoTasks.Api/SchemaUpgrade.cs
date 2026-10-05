using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
namespace NeoTasks;
public static class SchemaUpgrade {
 public static async Task Apply(TasksDb db) {
  await db.Database.OpenConnectionAsync();
  try {
   var connection=(NpgsqlConnection)db.Database.GetDbConnection();
   // Older releases created the same five domain tables with EnsureCreated.
   // Adopt that baseline without dropping any existing recruiter evaluation data.
   await using var check=new NpgsqlCommand("SELECT to_regclass('public.\"Users\"') IS NOT NULL AND to_regclass('public.\"__EFMigrationsHistory\"') IS NULL",connection);
   if((bool)(await check.ExecuteScalarAsync())!) {
    var baseline=db.Database.GetMigrations().Single(m=>m.EndsWith("_InitialTasks"));
    var history=db.GetService<IHistoryRepository>();await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
    await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(baseline,"10.0.12")));
   }
  } finally {await db.Database.CloseConnectionAsync();}
  await db.Database.MigrateAsync();
 }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NeoTasks;
public sealed partial class ApiTests {
 [Fact] public async Task Paginated_collections_search_all_records_and_audit_stays_within_organization(){
  using var owner=await Register("pages@example.test");using var other=await Register("otherpages@example.test");
  for(int i=0;i<23;i++)await owner.PostAsJsonAsync("/app-api/projects",new{name=$"Projeto {i:00}"});
  var first=await owner.GetAsync("/app-api/projects?page=1");var second=await owner.GetFromJsonAsync<JsonElement>("/app-api/projects?page=2");
  Assert.Equal("23",first.Headers.GetValues("X-Total-Count").Single());Assert.Equal(20,(await first.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());Assert.Equal(3,second.GetArrayLength());
  Assert.Single((await owner.GetFromJsonAsync<JsonElement>("/app-api/projects?q=22")).EnumerateArray());
  var ownAudit=await owner.GetFromJsonAsync<JsonElement>("/app-api/audit");var otherAudit=await other.GetFromJsonAsync<JsonElement>("/app-api/audit");
  Assert.True(ownAudit.GetProperty("total").GetInt32()>20);Assert.DoesNotContain("WorkProject",otherAudit.ToString());Assert.DoesNotContain("PasswordHash",ownAudit.ToString());
 }
 [Fact] public async Task Legacy_EnsureCreated_schema_upgrades_without_losing_projects(){
  using var client=factory.CreateClient();using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<TasksDb>();
  await db.Database.EnsureDeletedAsync();var baseline=db.Database.GetMigrations().Single(m=>m.EndsWith("_InitialTasks"));await db.GetService<IMigrator>().MigrateAsync(baseline);
  var org=Guid.NewGuid();var project=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Organizations\" (\"Id\",\"Name\") VALUES ({org},'Existing organization')");
  await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Projects\" (\"Id\",\"Name\",\"OrganizationId\") VALUES ({project},'Existing project',{org})");
  await db.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\"");await SchemaUpgrade.Apply(db);
  Assert.Equal("Existing project",(await db.Projects.SingleAsync(p=>p.Id==project)).Name);Assert.Empty(await db.Database.GetPendingMigrationsAsync());
 }
}

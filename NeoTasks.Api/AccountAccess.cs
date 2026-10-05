using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace NeoTasks;

public sealed class AccountAccess(TasksDb db,IConfiguration config,PasswordHasher<User> hasher,IWebHostEnvironment environment) {
 public string Jwt(User u) {
  var key=config["Jwt:Key"]??"local-demo-only-neotasks-key-32-characters";
  return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("neotasks","neotasks-api",
   [new("sub",u.Id.ToString()),new("org",u.OrganizationId.ToString()),new("role",u.Role),new("stamp",u.SecurityStamp)],
   expires:DateTime.UtcNow.AddMinutes(30),signingCredentials:new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),SecurityAlgorithms.HmacSha256)));
 }
 static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
 async Task<string> Issue(User user,string purpose,TimeSpan lifetime) {
  var raw=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
  db.AccessTokens.Add(new(){UserId=user.Id,Hash=Hash(raw),Purpose=purpose,ExpiresAt=DateTime.UtcNow.Add(lifetime)});
  await db.SaveChangesAsync();return raw;
 }
 public async Task StartSession(User user,HttpContext ctx) {
  var token=await Issue(user,"refresh",TimeSpan.FromDays(7));
  ctx.Response.Cookies.Append("neotasks.refresh",token,new(){HttpOnly=true,SameSite=SameSiteMode.Strict,Secure=ctx.Request.IsHttps,Path="/app-api",MaxAge=TimeSpan.FromDays(7)});
 }
 public async Task<User?> Consume(string raw,string purpose) {
  if(raw.Length!=64)return null;
  var token=await db.AccessTokens.AsNoTracking().SingleOrDefaultAsync(t=>t.Hash==Hash(raw)&&t.Purpose==purpose&&!t.Used&&t.ExpiresAt>DateTime.UtcNow);
  if(token is null)return null;
  // Atomically consume single-use tokens, including concurrent refresh requests.
  if(await db.AccessTokens.Where(t=>t.Id==token.Id&&!t.Used).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Used,true))!=1)return null;
  return await db.Users.SingleOrDefaultAsync(u=>u.Id==token.UserId);
 }
 public async Task SendLink(User user,string purpose) {
  var host=config["Mail:Host"];
  if(string.IsNullOrEmpty(host)&&environment.IsDevelopment())return;
  if(string.IsNullOrEmpty(host))throw new InvalidOperationException("Configure Mail__Host.");
  var raw=await Issue(user,purpose,TimeSpan.FromHours(1));
  var url=(config["PublicUrl"]??"http://localhost:8080").TrimEnd('/')+"/?action="+purpose+"&token="+raw;
  using var message=new MailMessage(config["Mail:From"]??"no-reply@neotasks.test",user.Email) {Subject=purpose=="verify"?"Confirme seu e-mail no NeoTasks":"Redefina sua senha no NeoTasks",Body=$"Olá, {user.Name}.\n\nAbra este link para {(purpose=="verify"?"confirmar seu e-mail":"escolher uma nova senha")}:\n{url}\n\nO link vale por uma hora e pode ser usado uma vez. Se você não pediu, ignore esta mensagem."};
  using var smtp=new SmtpClient(host,config.GetValue<int?>("Mail:Port")??1025){EnableSsl=config.GetValue<bool>("Mail:UseTls")};
  if(config["Mail:Username"] is string username)smtp.Credentials=new NetworkCredential(username,config["Mail:Password"]);
  await smtp.SendMailAsync(message);
 }
 public async Task Reset(User user,string password) {
  user.PasswordHash=hasher.HashPassword(user,password);user.SecurityStamp=Guid.NewGuid().ToString("N");
  foreach(var token in await db.AccessTokens.Where(t=>t.UserId==user.Id&&!t.Used).ToListAsync())token.Used=true;
  await db.SaveChangesAsync();
 }
}
public static class AccessEndpoints {
 public static void MapAccessEndpoints(this WebApplication app) {
  var routes=app.MapGroup("/app-api");
  routes.MapPost("/refresh",async(HttpContext ctx,AccountAccess access)=>{
   var u=await access.Consume(ctx.Request.Cookies["neotasks.refresh"]??"","refresh");if(u is null)return Results.Unauthorized();
   await access.StartSession(u,ctx);return Results.Ok(new{token=access.Jwt(u),username=u.Name,role=u.Role});
  }).RequireRateLimiting("access");
  routes.MapPost("/logout",async(HttpContext ctx,TasksDb db)=>{
   var raw=ctx.Request.Cookies["neotasks.refresh"];
   if(raw is not null){var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));await db.AccessTokens.Where(t=>t.Hash==hash).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Used,true));}
   ctx.Response.Cookies.Delete("neotasks.refresh",new(){Path="/app-api"});return Results.NoContent();
  });
  routes.MapPost("/forgot-password",async(EmailInput r,TasksDb db,AccountAccess access)=>{
   if(string.IsNullOrWhiteSpace(r.Email)||r.Email.Length>254)return Results.BadRequest();
   var u=await db.Users.SingleOrDefaultAsync(u=>u.Email==r.Email.Trim().ToLowerInvariant());if(u is not null)await access.SendLink(u,"reset");
   return Results.Ok(new{message="Se o e-mail estiver cadastrado, você receberá um link para redefinir a senha."});
  }).RequireRateLimiting("access");
  routes.MapPost("/reset-password",async(ResetInput r,AccountAccess access)=>{
   if(r.Password is not{Length:>=12 and <=128}||r.Token is null)return Results.BadRequest(new{error="Use uma senha de 12 a 128 caracteres."});
   var u=await access.Consume(r.Token,"reset");if(u is null)return Results.BadRequest(new{error="O link expirou ou já foi usado."});
   await access.Reset(u,r.Password);return Results.Ok(new{message="Senha alterada. Entre novamente."});
  }).RequireRateLimiting("access");
  routes.MapPost("/verify-email",async(TokenInput r,AccountAccess access,TasksDb db)=>{
   var u=await access.Consume(r.Token??"","verify");if(u is null)return Results.BadRequest(new{error="O link expirou ou já foi usado."});
   u.EmailVerified=true;await db.SaveChangesAsync();return Results.Ok(new{message="E-mail confirmado."});
  }).RequireRateLimiting("access");
  routes.MapGet("/account",async(ClaimsPrincipal actor,TasksDb db)=>{
   var u=await db.Users.AsNoTracking().SingleAsync(u=>u.Id==Guid.Parse(actor.FindFirstValue("sub")!));return Results.Ok(new{u.Name,u.Email,u.EmailVerified,u.Role});
  }).RequireAuthorization();
  routes.MapPost("/resend-verification",async(ClaimsPrincipal actor,TasksDb db,AccountAccess access)=>{
   var u=await db.Users.SingleAsync(u=>u.Id==Guid.Parse(actor.FindFirstValue("sub")!));if(!u.EmailVerified)await access.SendLink(u,"verify");return Results.Ok(new{message="Confira sua caixa de entrada."});
  }).RequireAuthorization().RequireRateLimiting("access");
  routes.MapGet("/audit",async(int? page,ClaimsPrincipal actor,TasksDb db)=>{
   var org=Guid.Parse(actor.FindFirstValue("org")!);var p=Math.Clamp(page??1,1,100000);var q=db.Audit.AsNoTracking().Where(a=>a.OrganizationId==org);
   return Results.Ok(new{items=await q.OrderByDescending(a=>a.Id).Skip((p-1)*20).Take(20).ToListAsync(),total=await q.CountAsync(),page=p});
  }).RequireAuthorization(p=>p.RequireRole("Owner"));
 }
}
public record EmailInput(string Email);
public record ResetInput(string Token,string Password);
public record TokenInput(string Token);

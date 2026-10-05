import {test,expect} from '@playwright/test';
test('email verification, rotating session and password recovery are usable end to end',async({page,request})=>{
 const email=`access-${Date.now()}@example.test`,password='AccessPassword123!';
 const registration=await request.post('/app-api/register',{data:{email,password,name:'Avaliador',organization:'Avaliação de acesso'}});expect(registration.status()).toBe(201);
 const initial=await registration.json();
 async function mailLink(subject:string){
  let id='';await expect.poll(async()=>{const list=await(await request.get('http://127.0.0.1:8025/api/v1/messages')).json();const message=list.messages.find((m:any)=>m.Subject===subject&&m.To.some((t:any)=>t.Address===email));id=message?.ID||'';return !!id;}).toBe(true);
  const message=await(await request.get(`http://127.0.0.1:8025/api/v1/message/${id}`)).json();return message.Text.match(/http:\/\/[^\s]+/)[0];
 }
 const confirmation=await mailLink('Confirme seu e-mail no NeoTasks');
 await page.goto(confirmation);await expect(page.getByRole('status')).toContainText('E-mail confirmado');
 expect((await(await request.get('/app-api/account',{headers:{Authorization:'Bearer '+initial.token}})).json()).emailVerified).toBe(true);
 const refreshCookie=(await request.storageState()).cookies.find(c=>c.name==='neotasks.refresh')!;expect(refreshCookie.httpOnly).toBe(true);
 const renewed=await request.post('/app-api/refresh');expect(renewed.status()).toBe(200);
 const replay=await request.post('/app-api/refresh',{headers:{Cookie:`neotasks.refresh=${refreshCookie.value}`}});expect(replay.status()).toBe(401);
 await page.getByRole('button',{name:'Esqueci minha senha'}).click();await page.getByLabel('E-mail',{exact:true}).fill(email);await page.getByRole('button',{name:'CONFIRMAR',exact:true}).click();await expect(page.getByRole('status')).toContainText('receberá um link');
 const recovery=await mailLink('Redefina sua senha no NeoTasks');await page.goto(recovery);await page.getByLabel('Nova senha').fill('RecoveredPassword123!');await page.getByRole('button',{name:'CONFIRMAR',exact:true}).click();await expect(page.getByRole('status')).toContainText('Senha alterada');
 expect((await request.get('/app-api/account',{headers:{Authorization:'Bearer '+initial.token}})).status()).toBe(401);
 await page.getByLabel('E-mail',{exact:true}).fill(email);await page.getByLabel('Senha',{exact:true}).fill('RecoveredPassword123!');await page.getByRole('button',{name:'ENTRAR',exact:true}).click();await expect(page.getByText('Olá, Avaliador!')).toBeVisible();
 // An expired access token must renew through the HttpOnly cookie without losing work.
 await page.evaluate(()=>sessionStorage.setItem('neotasks:token','expired'));await page.reload();await expect(page.getByText('Olá, Avaliador!')).toBeVisible();
 await expect.poll(()=>page.evaluate(()=>sessionStorage.getItem('neotasks:token'))).not.toBe('expired');
 const url=new URL(recovery);expect((await request.post('/app-api/reset-password',{data:{token:url.searchParams.get('token'),password:'AnotherPassword123!'}})).status()).toBe(400);
});

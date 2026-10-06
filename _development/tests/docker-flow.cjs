// 検証専用の新規コンテナとアカウントにだけ接続する。秘密は出力しない。
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { execFileSync } = require('child_process');
const environmentPath = path.resolve(process.argv[2]);
const root = path.dirname(environmentPath);
const { chromium } = require(path.join(root, 'browser/node_modules/playwright-core'));
const env = JSON.parse(fs.readFileSync(environmentPath, 'utf8').replace(/^\uFEFF/, ''));
const mainUrl = 'http://localhost:' + env.appPort;
const wrapper = 'http://localhost:15154';
const images = path.resolve('_documents/images');
const check = (value, message) => { if (!value) throw new Error(message); };
const sql = query => execFileSync('docker', ['exec', env.pg, 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', 'Implem.Pleasanter', '-At', '-c', query], {encoding:'utf8'}).trim();
const apiKey = login => sql(`SELECT "ApiKey" FROM "Implem.Pleasanter"."Users" WHERE "LoginId"='${login}'`);
const result = { pleasanter: '1.5.8.1', database:'PostgreSQL 17', checks: [] };
async function mainLogin(browser, loginId) {
 const context = await browser.newContext({viewport:{width:1100,height:800}});
 const page = await context.newPage();
 await page.goto(mainUrl + '/users/login');
 await page.locator('#Users_LoginId').fill(loginId);
 await page.locator('#Users_Password').fill(env.adminPassword);
 await page.locator('[data-action="Authenticate"]').click();
 await page.waitForURL(url => !url.pathname.toLowerCase().includes('login'));
 return {context,page};
}
function authorizationUrl(culture='ja') {
 const verifier = crypto.randomBytes(48).toString('base64url');
 const params = new URLSearchParams({client_id:'demo-client',response_type:'code',scope:'mcp offline_access',redirect_uri:'https://client.example/callback',state:'verification',resource:wrapper+'/mcp',code_challenge_method:'S256',code_challenge:crypto.createHash('sha256').update(verifier).digest('base64url'),culture});
 return {url:wrapper+'/connect/authorize?'+params,verifier};
}
async function authorize(browser,key,mode,shot) {
 const context = await browser.newContext({viewport:{width:1100,height:800}});
 await context.route('https://client.example/callback?*', route => route.fulfill({status:200,contentType:'text/plain',body:'検証用コールバック'}));
 const page = await context.newPage(); const auth=authorizationUrl();
 await page.goto(auth.url);
 if(shot) await page.screenshot({path:path.join(images,'login.png'),fullPage:true});
 await page.locator('[name=password]').fill(key);
 await page.locator(`[name=keyMode][value=${mode}]`).check();
 await page.locator('[name=decision][value=login]').click();
 await page.locator('[name=ticket]').waitFor({state:'attached'});
 if(shot) await page.screenshot({path:path.join(images,'consent-'+mode+'.png'),fullPage:true});
 await page.locator('[name=decision][value=approve]').click();
 await page.waitForURL(url=>url.hostname==='client.example');
 const code=new URL(page.url()).searchParams.get('code'); check(code,'認可コードがありません。');
 const token = await context.request.post(wrapper+'/connect/token',{form:{grant_type:'authorization_code',client_id:'demo-client',code,redirect_uri:'https://client.example/callback',code_verifier:auth.verifier,resource:wrapper+'/mcp'}});
 check(token.status()===200,'コード交換に失敗しました。');
 const payload=await token.json(); return {context, token:payload.access_token, refresh:payload.refresh_token};
}
async function mcp(client) {
 const headers={Authorization:'Bearer '+client.token,Accept:'application/json, text/event-stream'};
 const initialized=await client.context.request.post(wrapper+'/mcp',{headers,data:{jsonrpc:'2.0',id:1,method:'initialize',params:{protocolVersion:'2025-11-25',capabilities:{},clientInfo:{name:'WrapperDockerVerification',version:'1.0'}}}});
 check(initialized.status()===200,'MCP initialize に失敗しました。');
 const parse = text => JSON.parse(text.startsWith('{')?text:text.split('\n').find(x=>x.startsWith('data:')).substring(5));
 const init=parse(await initialized.text()); check(init.result,'initialize の結果がありません。');
 const session=initialized.headers()['mcp-session-id']; if(session)headers['Mcp-Session-Id']=session;
 headers['MCP-Protocol-Version']=init.result.protocolVersion;
 await client.context.request.post(wrapper+'/mcp',{headers,data:{jsonrpc:'2.0',method:'notifications/initialized'}});
 const listed=await client.context.request.post(wrapper+'/mcp',{headers,data:{jsonrpc:'2.0',id:2,method:'tools/list'}});
 check(listed.status()===200,'MCP tools/list に失敗しました。');
 const tools=parse(await listed.text()).result?.tools; check(tools?.length>0,'ツール一覧が空です。');
 console.log('MCP initialize と tools/list を確認:',tools.length,'tools');
 return {headers, tools};
}
(async () => {
 fs.mkdirSync(images,{recursive:true});
 const browser=await chromium.launch({executablePath:process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
 try {
  console.log('標準ログイン確認'); const admin=await mainLogin(browser,'Administrator');
  const adminKey=apiKey('Administrator'); check(adminKey.length>20,'先に標準画面で管理者の API キーを発行してください。');
  if(!sql('SELECT "UserId" FROM "Implem.Pleasanter"."Users" WHERE "LoginId"=\'demo-user\'')) {
   const created=await admin.context.request.post(mainUrl+'/api/users/create',{data:{ApiVersion:1.1,ApiKey:adminKey,LoginId:'demo-user',Name:'検証利用者',Password:env.adminPassword,DeptId:0,PasswordExpirationTime:'2999-01-01T00:00:00'}});
   const payload=await created.json(); check([200,201].includes(payload.StatusCode),'検証利用者の作成に失敗しました。status='+payload.StatusCode+' message='+payload.Message);
  }
  console.log('利用者ログイン確認'); const user=await mainLogin(browser,'demo-user');
  await user.page.goto(mainUrl+'/users/editapi');
  if(!apiKey('demo-user')) { await user.page.locator('#CreateApiKey').click(); await user.page.waitForFunction(()=>document.querySelector('#ApiKey')?.textContent.trim().length>20); }
  const personalKey=apiKey('demo-user'); check(personalKey.length>20,'利用者の API キーを発行できませんでした。');
  result.checks.push('標準ログイン・管理者と利用者のAPIキー発行');
  console.log('個人キー認可確認'); const personal=await authorize(browser,personalKey,'personal',true); await mcp(personal); result.checks.push('個人キーでOAuth認可・PKCE交換・MCP初期化とツール一覧');
  const shared=await authorize(browser,personalKey,'shared',true); await mcp(shared); result.checks.push('共通権限でOAuth認可・MCP初期化とツール一覧');
  const view=await browser.newPage({viewport:{width:1100,height:800}});
  await view.goto(authorizationUrl().url); await view.locator('[name=password]').fill('invalid-test-key'); await view.locator('[name=decision][value=login]').click(); await view.locator('[role=alert]').waitFor(); await view.screenshot({path:path.join(images,'login-error.png'),fullPage:true});
  for(const culture of ['ja','en','zh','de','ko','es','vi']) { await view.goto(authorizationUrl(culture).url); check(await view.locator('html').getAttribute('lang')===culture,'言語が一致しません。'); await view.screenshot({path:path.join(images,'login-'+culture+'.png'),fullPage:true}); }
  result.checks.push('7言語のログイン画面');
  // 再発行は Pleasanter の標準画面から行い、キーの値は記録しない。
  await user.page.locator('#CreateApiKey').click(); await user.page.waitForFunction(old=>document.querySelector('#ApiKey')?.textContent.trim()!==old,personalKey);
  const revoked=await personal.context.request.post(wrapper+'/mcp',{headers:{Authorization:'Bearer '+personal.token,Accept:'application/json, text/event-stream'},data:{jsonrpc:'2.0',id:3,method:'tools/list'}});
  check([401,403].includes(revoked.status()),'再発行後のアクセストークンが拒否されませんでした。');
  const refresh=await personal.context.request.post(wrapper+'/connect/token',{form:{grant_type:'refresh_token',client_id:'demo-client',refresh_token:personal.refresh}}); check(refresh.status()===400,'再発行後の更新トークンが拒否されませんでした。');
  result.checks.push('標準画面でのAPIキー再発行後、アクセス・更新トークン拒否');
  fs.writeFileSync(path.join(root,'results.json'),JSON.stringify(result,null,2));
  console.log('Docker 実機検証が完了しました。');
 } finally { await browser.close(); }
})().catch(e=>{console.error(e.name === 'TimeoutError' ? '画面遷移の制限時間を超過しました。' : e.message);process.exit(1)});

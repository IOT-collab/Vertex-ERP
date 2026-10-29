const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('Vertex ERP/wwwroot/js/notification-inbox.js', 'utf8');
let passed = 0;
function check(ok, message) { if (!ok) throw new Error(message); console.log('PASS ' + message); passed++; }
async function settle() { for (let i=0; i<8; i++) await Promise.resolve(); }
(async () => {
 const attrs = {}, badge = { textContent: '', hidden: true };
 const bell = { querySelector: () => badge, setAttribute: (k,v) => attrs[k]=v };
 let calls=0, options, timer, period, result={count:4}, html=false, fail=false;
 const events={};
 const document={hidden:false,querySelectorAll:()=>[bell],addEventListener:(k,v)=>events[k]=v};
 vm.runInNewContext(source,{document,window:{setInterval:(f,ms)=>{timer=f;period=ms;},addEventListener:(k,v)=>events[k]=v},fetch:async(url,opts)=>{calls++; options=opts; if(fail) throw new Error('offline'); return {ok:true,headers:{get:()=>html?'text/html':'application/json'},json:async()=>result};}});
 await settle();
 check(badge.textContent==='4' && !badge.hidden, 'Unread count displayed');
 check(attrs['aria-label']==='Notifications, 4 unread', 'Accessible unread label updated');
 check(options.cache==='no-store' && period===30000, 'Fresh polling every 30 seconds');
 result={count:0};timer();await settle();check(badge.hidden,'Zero unread hides badge');
 result={count:200};timer();await settle();check(badge.textContent==='99+','Large badge count stays compact');
 html=true;timer();await settle();check(badge.textContent==='99+','Expired login HTML does not break bell');
 html=false;result={count:-1};timer();await settle();check(badge.textContent==='99+','Invalid response preserves last valid count');
 fail=true;timer();await settle();check(badge.textContent==='99+','Temporary network failure preserves usable bell');
 document.hidden=true;const before=calls;timer();await settle();check(calls===before,'Hidden tabs do not poll');
 let emptyCalls=0;vm.runInNewContext(source,{document:{querySelectorAll:()=>[]},fetch:()=>emptyCalls++});check(emptyCalls===0,'Pages without bell perform no requests');
 console.log(`${passed} bell checks passed.`);
})().catch(error=>{console.error(error);process.exitCode=1;});

const fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('Vertex ERP/wwwroot/js/site-location-cards.js','utf8');
let passed=0;function check(ok,name){if(!ok)throw new Error(name);passed++;console.log('PASS '+name);}async function settle(){for(let i=0;i<15;i++)await Promise.resolve();}
(async()=>{
 let id=null,nextId='8',markup='check-in',fetches=0,replacements=0,timer,period,url,error=false,focus=null;const events={},windowEvents={},sidebar={scrollTop:37},status={textContent:''},date={value:'2026-10-29'},form={action:'http://local/Main/LocationTracking'};
 const content={querySelector:selector=>selector==='.sl-roster'?sidebar:selector.includes('aria-current')?(id?{dataset:{siteEmployee:id}}:null):null,replaceChildren:()=>{id=nextId;replacements++;},addEventListener:(k,v)=>events[k]=v};
 const page={querySelector:selector=>selector==='[data-site-location-content]'?content:selector==='.sl-date'?form:selector==='#sl-date'?date:status};
 const document={hidden:false,activeElement:null,querySelector:()=>page,addEventListener:(k,v)=>events[k]=v};
 class Parser{parseFromString(){return{querySelector:()=>({innerHTML:markup,childNodes:[{}]})};}}
 vm.runInNewContext(source,{document,window:{location:{origin:'http://local'},setInterval:(f,ms)=>{timer=f;period=ms;},addEventListener:(k,v)=>windowEvents[k]=v},URL,AbortController,DOMParser:Parser,history:{replaceState:(_,__,value)=>{url=new URL(value);}},fetch:async input=>{fetches++;url=new URL(input);return{ok:!error,text:async()=>'<main></main>'};}});
 check(period===15000,'Site attendance polls every 15 seconds');
 await timer();await settle();check(id==='8'&&replacements===1,'First site attendance automatically creates a card');
 await timer();await settle();check(replacements===1,'Unchanged responses preserve card, map and scroll without rebuilding');
 markup='check-out';await timer();await settle();check(id==='8'&&replacements===2,'Checkout replaces same employee content instead of appending duplicate cards');
 check(url.searchParams.get('employeeId')==='8'&&url.searchParams.get('date')==='2026-10-29','Selected employee and date retained in polling requests');
 error=true;await timer();await settle();check(replacements===2&&status.textContent.includes('retained'),'Network failure retains current employee details');error=false;
 const before=fetches;document.hidden=true;await timer();check(fetches===before,'Hidden page does not poll');document.hidden=false;
 date.value='2026-10-28';await timer();check(fetches===before,'Unsaved date selection is not overwritten by a refresh');date.value='2026-10-29';
 markup='other';nextId='9';await events.click({target:{closest:()=>({dataset:{siteEmployee:'9'}})},button:0,preventDefault(){}});await settle();check(id==='9'&&url.searchParams.get('employeeId')==='9','Card selection loads details and updates URL without page reload');
 check(sidebar.scrollTop===37,'Sidebar scroll position is preserved');
 console.log(`${passed} site refresh checks passed.`);
})().catch(error=>{console.error(error);process.exitCode=1;});

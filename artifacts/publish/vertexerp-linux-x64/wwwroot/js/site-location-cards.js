(() => {
 const page=document.querySelector('[data-site-location-page]');if(!page)return;
 const content=page.querySelector('[data-site-location-content]'),form=page.querySelector('.sl-date'),date=page.querySelector('#sl-date'),status=page.querySelector('[data-site-refresh-status]');
 let lastMarkup=null;
 let selected=content.querySelector('[data-site-employee][aria-current="true"]')?.dataset.siteEmployee||null,busy=false,controller=null;
 const loadedDate=date.value;
 async function load(employeeId,interactive=false){
  if(!interactive&&(busy||document.hidden||document.activeElement===date||date.value!==loadedDate))return;
  if(interactive)controller?.abort();
  const request=new AbortController();controller=request;busy=true;
  const url=new URL(form.action,window.location.origin);url.searchParams.set('date',loadedDate);if(employeeId)url.searchParams.set('employeeId',employeeId);
  try{
   const response=await fetch(url,{cache:'no-store',signal:request.signal,headers:{'X-Requested-With':'XMLHttpRequest'}});
   if(!response.ok)throw new Error('refresh');
   const documentNext=new DOMParser().parseFromString(await response.text(),'text/html'),next=documentNext.querySelector('[data-site-location-content]');if(!next)throw new Error('session');
   if(controller!==request)return;
   if(next.innerHTML!==lastMarkup){
    const sidebar=content.querySelector('.sl-roster'),scroll=sidebar?.scrollTop||0,focusId=document.activeElement?.closest('[data-site-employee]')?.dataset.siteEmployee;
    content.replaceChildren(...next.childNodes);lastMarkup=next.innerHTML;
    const updatedSidebar=content.querySelector('.sl-roster');if(updatedSidebar)updatedSidebar.scrollTop=scroll;
    selected=content.querySelector('[data-site-employee][aria-current="true"]')?.dataset.siteEmployee||null;
    if(focusId)content.querySelector(`[data-site-employee="${Number(focusId)}"]`)?.focus({preventScroll:true});
   }
   if(interactive){const actual=new URL(url);if(selected)actual.searchParams.set('employeeId',selected);else actual.searchParams.delete('employeeId');history.replaceState(null,'',actual);}
   status.textContent='Attendance updated · '+new Intl.DateTimeFormat('en-IN',{timeZone:'Asia/Kolkata',hour:'2-digit',minute:'2-digit',second:'2-digit'}).format(new Date())+' IST';
  }catch(error){if(controller===request&&error.name!=='AbortError')status.textContent='Unable to refresh right now. Existing details are retained; the page will retry.';}
  finally{if(controller===request)busy=false;}
 }
 content.addEventListener('click',event=>{const card=event.target.closest('[data-site-employee]');if(!card||event.ctrlKey||event.metaKey||event.shiftKey||event.altKey||event.button!==0)return;event.preventDefault();load(card.dataset.siteEmployee,true);});
 window.setInterval(()=>load(selected),15000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)load(selected);});window.addEventListener('pageshow',()=>load(selected));
})();

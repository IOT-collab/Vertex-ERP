(() => {
    'use strict';
    const indiaParts = date => Object.fromEntries(new Intl.DateTimeFormat('en-GB', {timeZone:'Asia/Kolkata',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',hourCycle:'h23'}).formatToParts(date).filter(x=>x.type!=='literal').map(x=>[x.type,x.value]));
    const greeting = date => { const hour=Number(indiaParts(date).hour); return hour<12?'Good Morning':hour<17?'Good Afternoon':'Good Evening'; };
    // Shared pure clock functions also support timezone-boundary regression checks.
    window.VertexManagerDashboardClock = {indiaParts, greeting};
    const root=document.getElementById('managerDashboard');
    if(!root) return;
    const get=id=>document.getElementById(id);
    let loadedDay=null, reloading=false;
    function updateClock() {
        const date=new Date(), parts=indiaParts(date), day=parts.year+'-'+parts.month+'-'+parts.day;
        const text=greeting(date);
        get('manager-greeting').textContent=text+',';
        root.dataset.period=text==='Good Morning'?'morning':text==='Good Afternoon'?'afternoon':'evening';
        get('manager-today').textContent=new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Kolkata',day:'2-digit',month:'long',year:'numeric'}).format(date);
        get('manager-weekday').textContent=new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Kolkata',weekday:'long'}).format(date);
        if(loadedDay && loadedDay!==day && !reloading) { reloading=true; window.location.reload(); }
        loadedDay=day;
    }
    updateClock();
    window.setInterval(updateClock,30000);
    document.addEventListener('visibilitychange',()=>{if(!document.hidden)updateClock();});
    window.addEventListener('focus',updateClock);

    const members=[...root.querySelectorAll('[data-manager-member]')];
    const teamSearch=get('manager-team-search');
    teamSearch.addEventListener('input',()=>{
        const term=teamSearch.value.trim().toLocaleLowerCase();let count=0;
        members.forEach(row=>{row.hidden=!row.dataset.search.toLocaleLowerCase().includes(term);if(!row.hidden)count++;});
        get('manager-no-members').hidden=count>0;
        if(members.length)get('manager-no-members').querySelector('strong').textContent='No matching employees';
        get('manager-team-count').textContent=count+' of '+members.length+' team members';
    });
    const globalSearch=get('manager-global-search'), results=get('manager-search-results');
    const searchData=JSON.parse(get('manager-search-data').textContent);
    if(globalSearch && results) {
        globalSearch.addEventListener('input',()=>{
            const term=globalSearch.value.trim().toLocaleLowerCase(); results.replaceChildren();
            results.hidden=!term;globalSearch.setAttribute('aria-expanded',String(!!term));
            if(!term)return;
            const matches=searchData.filter(item=>item.label.toLocaleLowerCase().includes(term)).slice(0,8);
            if(!matches.length){const p=document.createElement('p');p.textContent='No matching employees, projects or tasks';results.append(p);}
            matches.forEach(item=>{
                const link=document.createElement('a'), title=document.createElement('strong'), type=document.createElement('small');
                title.textContent=item.label;type.textContent=item.type;link.href=item.url;link.append(title,type);
                link.addEventListener('click',()=>{results.hidden=true;globalSearch.setAttribute('aria-expanded','false');if(item.url.startsWith('#')){teamSearch.value='';teamSearch.dispatchEvent(new Event('input'));}});results.append(link);
            });
        });
        globalSearch.addEventListener('keydown',event=>{if(event.key==='Escape'){results.hidden=true;globalSearch.setAttribute('aria-expanded','false');}});
        document.addEventListener('click',event=>{if(!event.target.closest('.md-global-search')){results.hidden=true;globalSearch.setAttribute('aria-expanded','false');}});
    }
    const approvalUrl=get('manager-recent-all').href;
    function activateTaskTab(value) {
        root.querySelectorAll('[data-manager-tab]').forEach(button=>button.setAttribute('aria-selected',String(button.dataset.managerTab===value)));
        root.querySelectorAll('[data-manager-task-panel]').forEach(panel=>panel.hidden=panel.dataset.managerTaskPanel!==value);
        if(value!=='approvals')get('manager-recent-all').href='/Main/TaskMgm';
        else get('manager-recent-all').href=approvalUrl;
    }
    root.querySelectorAll('[data-manager-tab]').forEach(button=>button.addEventListener('click',()=>activateTaskTab(button.dataset.managerTab)));
    root.querySelectorAll('[data-manager-open-tab]').forEach(button=>button.addEventListener('click',()=>{activateTaskTab(button.dataset.managerOpenTab);get('manager-recent').scrollIntoView({behavior:'smooth',block:'nearest'});}));
    const queryAll=root.querySelector('.md-query-heading>.md-soft-action'), openQueryUrl=queryAll.href;
    root.querySelectorAll('[data-manager-query]').forEach(button=>button.addEventListener('click',()=>{
        const value=button.dataset.managerQuery;
        root.querySelectorAll('[data-manager-query]').forEach(item=>item.setAttribute('aria-selected',String(item===button)));
        root.querySelectorAll('[data-manager-query-panel]').forEach(panel=>panel.hidden=panel.dataset.managerQueryPanel!==value);
        queryAll.href=value==='Open'?openQueryUrl:'/Main/ClosedIssues?status='+encodeURIComponent(value);
    }));
    root.querySelectorAll('[role="tablist"]').forEach(group=>group.addEventListener('keydown',event=>{
        if(!['ArrowRight','ArrowLeft','Home','End'].includes(event.key))return;
        const tabs=[...group.querySelectorAll('[role="tab"]')], current=tabs.indexOf(document.activeElement);
        if(current<0)return;
        const index=event.key==='Home'?0:event.key==='End'?tabs.length-1:(current+(event.key==='ArrowRight'?1:-1)+tabs.length)%tabs.length;
        event.preventDefault();tabs[index].focus();tabs[index].click();
    }));
    const chartData=JSON.parse(get('manager-chart-data').textContent), period=get('manager-chart-period');
    function updateChart() {
        const data=chartData[period.value], counts=[data.completed,data.inProgress,data.pending,data.overdue];
        const total=counts.reduce((sum,n)=>sum+n,0), colors=['var(--md-completed)','var(--md-progress)','var(--md-pending)','var(--md-overdue)'];
        let angle=0;const stops=[];
        counts.forEach((count,index)=>{const next=angle+(total?count/total*360:0);if(count)stops.push(colors[index]+' '+angle+'deg '+next+'deg');angle=next;});
        get('manager-workload-chart').style.setProperty('--md-chart-bg',total?'conic-gradient('+stops.join(',')+')':'var(--erp-surface-soft)');
        get('manager-chart-total').textContent=total;
        const names=['Completed','In Progress','Pending','Overdue'];
        root.querySelectorAll('[data-manager-category]').forEach((row,index)=>{row.querySelector('[data-chart-count]').textContent=counts[index];row.querySelector('[data-chart-percent]').textContent=(total?Math.round(counts[index]/total*100):0)+'%';});
        get('manager-workload-chart').setAttribute('aria-label',total+' total tasks: '+counts.map((n,i)=>n+' '+names[i]).join(', '));
        get('manager-chart-caption').textContent=period.value==='month'?'Tasks created this month':period.value==='week'?'Tasks created this week':'Task distribution across all time';
    }
    period.addEventListener('change',updateChart);updateChart();
    document.querySelector('[data-manager-fullscreen]')?.addEventListener('click',async()=>{
        try { if(document.fullscreenElement)await document.exitFullscreen();else await document.documentElement.requestFullscreen(); } catch {}
    });
    document.addEventListener('click',event=>document.querySelectorAll('.md-account[open],.md-notifications[open],.md-row-menu[open]').forEach(menu=>{if(!menu.contains(event.target))menu.open=false;}));
    document.addEventListener('keydown',event=>{if(event.key==='Escape')document.querySelectorAll('.md-account[open],.md-notifications[open],.md-row-menu[open]').forEach(menu=>menu.open=false);});
    const since=new Date().toISOString();
    window.setInterval(async()=>{
        if(document.hidden)return;
        try {const response=await fetch(root.dataset.notificationUrl+'?sinceUtc='+encodeURIComponent(since),{cache:'no-store'});if(response.ok && (await response.json()).hasNew)window.location.reload();}catch{}
    },30000);
})();

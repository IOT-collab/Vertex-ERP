from http.server import HTTPServer,BaseHTTPRequestHandler
from pathlib import Path
import re
root=Path('Vertex ERP').resolve()
base=['style.css','site.css','common-ui.css','erp-buttons.css']
def links(names):return ''.join('<link rel="stylesheet" href="/css/'+n+'">' for n in names)
def shell(body,css):
 return '<!doctype html><html><head><meta name="viewport" content="width=device-width, initial-scale=1">'+links(base)+'</head><body class="erp-shell"><input type="checkbox" id="sidebar-toggle" class="sidebar-toggle-check"><div class="layout"><label for="sidebar-toggle" class="sidebar-overlay"></label><aside class="sidebar"><div class="logo">Vertex ERP<label for="sidebar-toggle" class="sidebar-close-btn">×</label></div><ul class="sidebar-menu"><li><a href="/">Dashboard</a></li></ul></aside><main class="main-content"><header class="topbar"><div class="topbar-left"><label for="sidebar-toggle" class="hamburger-menu-btn"><span></span><span></span><span></span></label><a class="topbar-brand">Vertex ERP</a></div><div class="profile"><span class="profile-text">Welcome A very long employee display name</span><form><button class="logout-btn" type="button">Logout</button></form></div></header><div class="page-content">'+links(css)+body+'</div></main></div></body></html>'
calendar='<div class="calendar-widget"><div class="calendar-month-nav">August 2026</div><div class="calendar-days-grid">'+''.join('<span class="day-head">'+d+'</span>' for d in ['S','M','T','W','T','F','S'])+''.join('<span class="day-num">'+str(i)+'</span>' for i in range(1,32))+'</div></div>'
rows=''.join('<tr><td>Employee '+str(i)+'</td><td>Engineering and site operations</td><td>Pending review</td><td>31 August 2026</td><td><button type="button">View details</button></td></tr>' for i in range(1,15))
table='<div class="table-responsive"><table><thead><tr><th>Employee</th><th>Department</th><th>Status</th><th>Updated</th><th>Action</th></tr></thead><tbody>'+rows+'</tbody></table></div>'
fixture=shell('<div class="leave-dashboard-wrapper"><div class="dashboard-header"><h2>Attendance Management</h2></div><div class="dashboard-grid"><section><h3>Leave request</h3><div class="form-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:12px"><label>From<input type="date"></label><label>To<input type="date"></label><label style="grid-column:span 2">Reason<textarea style="width:100%"></textarea></label></div><label><input type="checkbox"> Half day</label></section><section>'+calendar+'</section></div>'+table+'</div>',['EmpLeaveManagement.css'])
def modal(kind):
 css,prefix={'report':('HrmReports.css','rpt-modal'),'expense':('ExpenseClaim.css','exp-modal'),'employee':('Employee11.css','report-modal'),'task':('TaskMgm.css','modal')}[kind]
 panel='modal-box' if kind=='task' else prefix
 return shell('<h1>Dialog preview</h1><div class="'+prefix+'-overlay" style="display:flex;opacity:1;pointer-events:auto"><div class="'+panel+'"><div class="'+prefix+'-header"><h2>Request details</h2><button type="button" class="'+prefix+'-close" aria-label="Close">×</button></div><div class="'+prefix+'-body"><label>Employee<input type="text" value="Example employee"></label><label>Reason<textarea>Example request details</textarea></label>'+''.join('<p>Request information line '+str(i)+'</p>' for i in range(20))+'</div><div class="'+prefix+'-footer"><button type="button">Cancel</button><button type="button">Save</button></div></div></div>',[css])
login=(root/'Views/Main/Login.cshtml').read_text(encoding='utf-8-sig')
login=login[login.index('<!DOCTYPE html>'):].replace('~/','/')
login=re.sub(r'@if \(ViewBag.ErrorMessage != null\).*?\n\s*}', '',login,flags=re.S)
class Handler(BaseHTTPRequestHandler):
 def do_GET(self):
  route=self.path.split('?')[0]
  if route.startswith('/css/') or route.startswith('/images/'):
   p=(root/'wwwroot'/route.lstrip('/')).resolve()
   if not p.is_relative_to(root/'wwwroot') or not p.is_file():self.send_error(404);return
   data=p.read_bytes();ctype='text/css' if p.suffix=='.css' else 'image/png'
  else:
   page=login if route=='/login' else modal(route[1:]) if route[1:] in ['report','expense','employee','task'] else fixture
   data=page.encode();ctype='text/html; charset=utf-8'
  self.send_response(200);self.send_header('Content-Type',ctype);self.end_headers();self.wfile.write(data)
 def log_message(self,*args):pass
HTTPServer(('127.0.0.1',8765),Handler).serve_forever()

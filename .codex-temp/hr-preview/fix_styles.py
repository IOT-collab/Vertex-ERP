from pathlib import Path
import re
changed=[]
for p in Path('Vertex ERP/wwwroot/css').glob('*.css'):
 s=p.read_text(encoding='utf-8-sig');t=re.sub(r'minmax\((\d+px),\s*1fr\)',r'minmax(min(100%, \1), 1fr)',s)
 if t!=s:p.write_text(t,encoding='utf-8');changed.append(p.name)
for name in ['_Layout.cshtml','_LayoutMaster.cshtml']:
 p=Path('Vertex ERP/Views/Shared')/name;s=p.read_text(encoding='utf-8')
 if '~/css/hr-modules.css' not in s:s=s.replace('</head>','    <link href="~/css/hr-modules.css" rel="stylesheet" asp-append-version="true" />\n</head>')
 if name=='_Layout.cshtml' and 'font-awesome/6.4.0' not in s:s=s.replace('</head>','    <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css" />\n</head>')
 p.write_text(s,encoding='utf-8')
print('Updated remaining grid widths and connected HR stylesheet:',changed)

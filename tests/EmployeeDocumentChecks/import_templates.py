"""Import the eight supplied letters without rewriting their clauses.

Usage: python import_templates.py INPUT_DIRECTORY CONTENT_ROOT
Only company names and employee/salary placeholders change. Source text and
paragraph IDs are retained beside every imported paragraph for content audits.
"""
import json
import re
import sys
from pathlib import Path
from zipfile import ZipFile
import xml.etree.ElementTree as ET

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
FILES = {
    'VPC-Relieving': 'Power Reliving or Experience letter.docx',
    'VPC-Joining': 'Power joining letter.docx',
    'VPC-LOI': 'Power LOI FORMAT.docx',
    'VPC-Offer': 'Power offer letter.docx',
    'VAS-Relieving': 'Automation Relieving letter.docx',
    'VAS-Joining': 'Automation Joining letter.docx',
    'VAS-LOI': 'Automation LOI.docx',
    'VAS-Offer': 'Automation Offer Letter.docx',
}

def normalize(text):
    return re.sub(r'\s+', ' ', text).strip()

def company(text):
    return re.sub(r'Vertex\s+(?:Power\s+Controls|Automation\s+Systems?)\s+Pvt\.?\s*Ltd\.?', '{{CompanyName}}', text)

def replace_fields(text, kind):
    text = company(text)
    if re.match(r'^(Date|Dated)\s*[:\-]', text):
        return re.sub(r'[:\-].*', ': {{IssueDate}}', text, count=1)
    if kind == 'Offer':
        text = re.sub(r'(Dear Mr\.\s*|^Mr\.\s*)[….]+', r'\1{{EmployeeName}}', text)
        text = re.sub(r'(?<=Address-)[….]+', '{{PresentAddress}}', text)
        text = re.sub(r'(?<=duty on )\s*[….]+', '{{EffectiveDate}}.', text)
        text = re.sub(r'(?<=position of )[….]+', '{{Designation}}', text)
        text = text.replace('As A Designation', 'As {{Designation}}')
        text = re.sub(r'(?<=Rs)[….]+', '{{MonthlyCtc}}', text)
    elif kind == 'LOI':
        text = re.sub(r'(?<=Mr\. )-+', '{{EmployeeName}}', text)
        text = text.replace('“--------------”', '“{{Designation}}”').replace('“----------------”', '“{{Department}}”')
        text = re.sub(r'(?<=duties from )-+', '{{EffectiveDate}}', text)
    elif kind == 'Relieving':
        text = re.sub(r'(?<=Name: - )-+', '{{EmployeeName}}', text)
        text = text.replace('(--/--/----)', '({{ResignationDate}})')
        text = text.replace('“------------------------”', '“{{Designation}}”')
        text = text.replace('from --/--/---- until --/--/----', 'from {{JoiningDate}} until {{EffectiveDate}}')
        text = text.replace('--/--/----', '{{EffectiveDate}}')
    elif kind == 'Joining':
        if text.startswith('Ref:'):
            text = 'Ref: {{CompanyReference}} /HR & Admin/{{FinancialYear}}/{{EmployeeCode}}'
        elif text.startswith('DOJ:'): text = 'DOJ: {{EffectiveDate}}'
        elif text.startswith('Kind Attn:'): text = 'Kind Attn: Mr. {{EmployeeName}}'
        elif text.startswith('DOB:'): text = 'DOB: {{DateOfBirth}}'
        elif text.startswith('Aadhar no.'): text = 'Aadhar no. {{AadhaarNumber}}'
        elif text.startswith('S/o Mr'): text = 'S/o Mr {{FatherName}}\n{{PermanentAddress}}'
        elif text.startswith('Vill.'): text = 'Vill. {{PresentAddress}}'
        elif text.startswith('Mob NO.'): text = 'Mob NO. {{Mobile}}'
        elif text == 'DOJ.': text = '{{EffectiveDate}}.'
        text = text.replace('(“Designation)- (Department)', '({{Designation}})- ({{Department}})')
        text = text.replace('(--------------)/-(--------------------------Rupees Only)', '({{AnnualCtc}})/-({{AnnualCtcWords}} Rupees Only)')
    return text

def main():
    source, root = map(Path, sys.argv[1:3])
    target = root/'DocumentTemplates'/'CompanyLetters'
    target.mkdir(parents=True, exist_ok=True)
    for key, filename in FILES.items():
        kind = key.split('-')[1]
        with ZipFile(source/filename) as archive:
            tree = ET.fromstring(archive.read('word/document.xml'))
            all_p = list(tree.iter(W+'p'))
            ids = {id(p): i for i,p in enumerate(all_p)}
            # Resolve Word's automatic list numbering, which is not stored in w:t.
            numbering = ET.fromstring(archive.read('word/numbering.xml')) if 'word/numbering.xml' in archive.namelist() else ET.Element('none')
            counters = {}
            def list_prefix(p):
                np = p.find(W+'pPr/'+W+'numPr')
                if np is None: return ''
                num = np.find(W+'numId').get(W+'val')
                level = np.find(W+'ilvl').get(W+'val') if np.find(W+'ilvl') is not None else '0'
                definition = numbering.find(f"{W}num[@{W}numId='{num}']/{W}abstractNumId")
                if definition is None: return ''
                lvl = numbering.find(f"{W}abstractNum[@{W}abstractNumId='{definition.get(W+'val')}']/{W}lvl[@{W}ilvl='{level}']")
                if lvl is None: return ''
                fmt = lvl.find(W+'numFmt').get(W+'val')
                count = counters.get((num,level), int(lvl.find(W+'start').get(W+'val')) - 1) + 1
                counters[num,level] = count
                if fmt == 'bullet': return '• '
                number = chr(96+count) if fmt == 'lowerLetter' else str(count)
                if fmt == 'lowerRoman':
                    number = ['i','ii','iii','iv','v','vi','vii','viii','ix','x','xi','xii'][count-1] if count <= 12 else str(count)
                return re.sub(r'%\d', number, lvl.find(W+'lvlText').get(W+'val'))+' '
            paragraphs = {}
            for p in all_p:
                raw = ''.join(n.text or '' for n in p.iter(W+'t'))
                text = normalize(raw)
                prefix = list_prefix(p)
                title = text in ('APPOINTMENT LETTER', 'Letter of Offer for Employment', 'Letter of Intent (LOI)', 'Relieving & Experience Letter')
                bold = bool(text) and (text.isupper() or title or (len(text)<95 and any(r.find(W+'rPr/'+W+'b') is not None for r in p.findall(W+'r'))))
                paragraphs[id(p)] = {'SourceId': ids[id(p)], 'SourceText': raw, 'Text': prefix + replace_fields(text,kind), 'Bold': bold, 'Title': title}
            def paras(element):
                return [paragraphs[id(p)].copy() for p in element.findall(W+'p') if paragraphs[id(p)]['Text'].strip()]
            blocks=[]
            table_index=0
            for element in tree.find(W+'body'):
                if element.tag == W+'p':
                    p=paragraphs[id(element)]
                    if p['Text'].strip(): blocks.append({'Paragraph': p})
                elif element.tag == W+'tbl':
                    rows=[]
                    for row in element.findall(W+'tr'):
                        cells=[]
                        for cell in row.findall(W+'tc'):
                            span=cell.find(W+'tcPr/'+W+'gridSpan')
                            cells.append({'Span': int(span.get(W+'val')) if span is not None else 1, 'Paragraphs': paras(cell)})
                        rows.append(cells)
                    if kind=='Joining':
                        def setcell(r,c,value):
                            existing=rows[r][c]['Paragraphs']
                            if existing: existing[0]['Text']=value
                            else: rows[r][c]['Paragraphs']=[{'SourceId':-1,'SourceText':'','Text':value}]
                        if table_index==0:
                            for c,token in [(0,'PermanentPinCode'),(1,'PresentPinCode')]:
                                for p in rows[1][c]['Paragraphs']:
                                    if p['Text'].startswith('Pin'): p['Text']='Pin {{'+token+'}}'
                        if table_index==1:
                            for r,c,token in [(2,1,'EmployeeName'),(2,4,'Designation'),(3,1,'Department'),(3,4,'ManagerName')]: setcell(r,c,'{{'+token+'}}')
                            for r,c,token in [(7,1,'Basic'),(7,5,'EmployeePf'),(8,1,'HraOther'),(8,5,'EmployeeEsi'),(9,5,'EmployeeContribution'),(10,1,'Fixed'),(13,5,'EmployerPf'),(15,5,'EmployerEsi'),(16,5,'EmployerContribution'),(18,1,'Epr'),(19,1,'OtherBenefits'),(20,5,'Net'),(21,1,'Gross'),(21,5,'Ctc')]:
                                setcell(r,c,'{{'+token+'Monthly}}');setcell(r,c+1,'{{'+token+'Yearly}}')
                        if table_index==3:
                            setcell(2,1,'{{EmployeeName}}');setcell(2,4,'{{EmployeeCode}}')
                    widths=[int(n.get(W+'w')) for n in element.findall(W+'tblGrid/'+W+'gridCol')]
                    blocks.append({'Table':{'Widths':widths,'Rows':rows,'Kind': ['Address','Salary','Grades','Voucher'][table_index] if kind=='Joining' else 'Table'}})
                    table_index+=1
            payload={'SourceFile':filename,'CompanyCode':key[:3],'Kind':kind,'SourceParagraphCount':sum(bool(normalize(''.join(n.text or '' for n in p.iter(W+'t')))) for p in all_p),'Blocks':blocks}
            (target/(key+'.json')).write_text(json.dumps(payload,ensure_ascii=False,indent=2),encoding='utf-8')
            print(key, payload['SourceParagraphCount'], 'source paragraphs imported')
            if key=='VPC-Offer':
                (target/'Power-Header.jpeg').write_bytes(archive.read('word/media/image1.jpeg'))

if __name__=='__main__': main()

import json, re, requests, hashlib
from pathlib import Path
from urllib.parse import urljoin, urlparse
from datetime import timedelta
exec(Path('tmp/check_managers.py').read_text().split('try:')[0])
c=config['RemoteAttendance']; base=c['BaseUrl']; s=requests.Session()
login=urljoin(base,'api-auth/login/?next=/iclock/api/')
r=s.get(login,timeout=20); r.raise_for_status()
token=re.search(r'name=[\"\x27]csrfmiddlewaretoken[\"\x27][^>]*value=[\"\x27]([^\"\x27]+)',r.text).group(1)
r=s.post(login,data={'username':c['Username'],'password':c['Password'],'csrfmiddlewaretoken':token,'next':'/iclock/api/'},headers={'Referer':login},timeout=20); r.raise_for_status()
with psycopg.connect(**options) as db:
 db.execute('SET TRANSACTION READ ONLY')
 latest=db.execute('SELECT max("PunchTime") FROM "AttendanceLogs" WHERE "RawPayload" LIKE \'REMOTE|%%\' AND "SourceIpAddress"=%s',(urlparse(base).hostname,)).fetchone()[0]
 params={'page_size':1000,'ordering':'punch_time,id'}
 if latest: params['start_time']=(latest-timedelta(days=1)).strftime('%Y-%m-%d %H:%M:%S')
 r=s.get(urljoin(base,'iclock/api/transactions/'),params=params,timeout=30); r.raise_for_status(); data=r.json()
 print('field_types', {k: sorted(set(type(x.get(k)).__name__ for x in data.get('data',[]))) for k in ['id','emp_code','punch_time','punch_state','verify_type','work_code','terminal_sn']})
 devices=dict(db.execute('SELECT "SerialNumber","Id" FROM "BiometricDevices" WHERE "IsActive"').fetchall())
 collisions=0; pending=0; seen=set()
 for x in data.get('data',[]):
  device=devices.get(x.get('terminal_sn','').strip())
  if not device: continue
  hash=hashlib.sha256(('REMOTE|'+str(device)+'|'+str(x['id'])).encode()).hexdigest().upper()
  if db.execute('SELECT 1 FROM "AttendanceLogs" WHERE "UniqueHash"=%s',(hash,)).fetchone(): continue
  pending+=1
  key=(device,x['emp_code'].strip(),x['punch_time'])
  if key in seen or db.execute('SELECT 1 FROM "AttendanceLogs" WHERE "BiometricDeviceId"=%s AND "DeviceUserId"=%s AND "PunchTime"=%s',key).fetchone(): collisions+=1
  seen.add(key)
 print(json.dumps({'checkpoint':str(latest),'page_rows':len(data.get('data',[])),'new_hash_rows':pending,'duplicate_punch_key_conflicts':collisions,'has_next_page':bool(data.get('next'))}))

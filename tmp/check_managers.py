import json, os
from pathlib import Path
import psycopg

root = Path('Vertex ERP')
config = json.loads((root / 'appsettings.json').read_text(encoding='utf-8-sig'))
environment = os.environ.get('ASPNETCORE_ENVIRONMENT', 'Development')
extra = root / f'appsettings.{environment}.json'
if extra.exists():
    config.setdefault('ConnectionStrings', {}).update(json.loads(extra.read_text(encoding='utf-8-sig')).get('ConnectionStrings', {}))
connection = os.environ.get('ConnectionStrings__DefaultConnection') or config['ConnectionStrings']['DefaultConnection']
parts = dict(part.split('=', 1) for part in connection.split(';') if '=' in part)
parts = {key.strip().lower(): value.strip() for key, value in parts.items()}
options = {target: parts[source] for source, target in [('host','host'), ('port','port'), ('database','dbname'), ('username','user'), ('password','password')] if source in parts}
options['connect_timeout'] = 10
try:
    with psycopg.connect(**options) as db:
        db.execute('SET TRANSACTION READ ONLY')
        rows = db.execute('''SELECT e."EmployeeCode", e."FullName", e."IsActive",
            EXISTS(SELECT 1 FROM "AppUsers" u WHERE u."EmployeeId"=e."Id" AND u."Role"='Manager' AND u."IsActive"),
            (SELECT count(*) FROM "Employees" t WHERE t."ReportingManagerId"=e."Id" AND t."IsActive"),
            (SELECT count(*) FROM "Projects" p WHERE p."ManagerId"=e."Id"),
            (SELECT count(*) FROM "Departments" d WHERE d."ManagerId"=e."Id")
            FROM "Employees" e WHERE
            EXISTS(SELECT 1 FROM "AppUsers" u WHERE u."EmployeeId"=e."Id" AND u."Role"='Manager')
            OR EXISTS(SELECT 1 FROM "Employees" t WHERE t."ReportingManagerId"=e."Id")
            OR EXISTS(SELECT 1 FROM "Projects" p WHERE p."ManagerId"=e."Id")
            OR EXISTS(SELECT 1 FROM "Departments" d WHERE d."ManagerId"=e."Id")
            ORDER BY e."FullName"''').fetchall()
        print(json.dumps({'environment': environment, 'managers': [dict(zip(['code','name','active_employee','active_manager_login','active_team_count','project_count','department_count'], row)) for row in rows]}, ensure_ascii=True))
except Exception as error:
    print('Database check failed: ' + type(error).__name__)
    if isinstance(error, psycopg.errors.UndefinedTable):
        print(error.diag.message_primary)
    raise SystemExit(1)

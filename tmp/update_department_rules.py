from pathlib import Path

p = Path('Vertex ERP/Controllers/HrController.cs')
data = p.read_bytes()
start = data.index(b'        public async Task<IActionResult> AddDepartment(AddDepartmentViewModel model)')
end = data.index(b'        [HttpGet]', start)
edit_start = data.index(b'        public async Task<IActionResult> EditDepartment(int id, AddDepartmentViewModel model)')
edit_end = data.index(b'        [HttpGet]', edit_start)

def update(block):
    text = block.decode('latin1').replace('\r\n', '\n')
    text = text.replace('DepartmentName.ToLower()', 'DepartmentName.Trim().ToLower()').replace('DepartmentCode.ToLower()', 'DepartmentCode.Trim().ToLower()')
    opening = text.index('        {\n') + len('        {\n')
    closing = text.rindex('        }')
    body = text[opening:closing]
    body = body.replace('await _dbContext.SaveChangesAsync();', 'await _dbContext.SaveChangesAsync();\n            await transaction.CommitAsync();')
    # A table lock covers the duplicate check and save, including concurrent add/edit requests.
    wrapper = '''            return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync<IActionResult>(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync();
                // Serialize the duplicate check and save across simultaneous department writes.
                await _dbContext.Database.ExecuteSqlRawAsync("LOCK TABLE \\"Departments\\" IN SHARE ROW EXCLUSIVE MODE");
'''
    body = '\n'.join('    ' + line if line else '' for line in body.split('\n'))
    result = text[:opening] + wrapper + body + '            });\n' + text[closing:]
    return result.replace('\n', '\r\n').encode('latin1')

data = data[:edit_start] + update(data[edit_start:edit_end]) + data[edit_end:]
data = data[:start] + update(data[start:end]) + data[end:]
p.write_bytes(data)

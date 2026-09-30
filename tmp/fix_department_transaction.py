from pathlib import Path
p = Path('Vertex ERP/Controllers/HrController.cs')
data = p.read_bytes()
data = data.replace(b'                    await _dbContext.SaveChangesAsync();\r\n                await transaction.CommitAsync();', b'                    await _dbContext.SaveChangesAsync();\r\n                    await transaction.CommitAsync();')
needle = b'                    _logger.LogError(exception, "Database error while adding department'
data = data.replace(needle, b'                    await transaction.RollbackAsync();\r\n' + needle)
needle = b'                // Serialize the duplicate check and save across simultaneous department writes.'
data = data.replace(needle, b'                _dbContext.ChangeTracker.Clear();\r\n' + needle)
p.write_bytes(data)

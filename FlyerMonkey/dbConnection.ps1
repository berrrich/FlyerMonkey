# Install the module if needed # 
Install-Module -Name SqlServer 
$server = "flyermonkeyazserver.database.windows.net" 
$database = "flyermonkeyazdb" 
$user = "azureuser" 
$password = "Blueredgreen101!" 
$connectionString = "Server=$server;Database=$database;User ID=$user;Password=$password;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" 

try { Invoke-Sqlcmd -ConnectionString $connectionString -Query "SELECT 1" Write-Host "Connection successful!" } catch { Write-Host "Connection failed: $_" }
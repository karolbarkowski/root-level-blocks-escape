$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try { node server.mjs }
finally { Pop-Location }

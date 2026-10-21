<#
.SYNOPSIS
    Entrega de sOC Scanner: versión, banco de pruebas, exe autocontenido y, sin -SoloCompilar,
    OneDrive, commit, push y release de GitHub (como sOC ScreenShare y RC Manager).
.DESCRIPTION
    El CHANGELOG y src\Scanner.Core\whatsnew.json se escriben antes a mano. -SoloCompilar deja solo
    el exe en publish\app, sin OneDrive ni git. -SinGit copia a OneDrive pero no commitea, ni sube ni
    publica la release (para cuando el repositorio de GitHub aún no existe).
.EXAMPLE
    .\tools\entregar.ps1 -Version 2026.10.21.0 -SoloCompilar
.EXAMPLE
    .\tools\entregar.ps1 -Version 2026.10.22.0 -Mensaje "2026.10.22.0: ..."
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Mensaje = '',
    [switch] $SoloCompilar,
    [switch] $SinGit
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
[IO.Directory]::SetCurrentDirectory($raiz)

# 1. Versión (una sola, en Directory.Build.props) y novedades.
$p = 'Directory.Build.props'; $x = Get-Content $p -Raw
foreach ($t in 'Version', 'AssemblyVersion', 'FileVersion') { $x = [regex]::Replace($x, "<$t>[^<]*</$t>", "<$t>$Version</$t>") }
[IO.File]::WriteAllText((Resolve-Path $p), $x)
$primera = (Get-Content src\Scanner.Core\whatsnew.json -Raw | ConvertFrom-Json)[0].version
if ([version]$primera -ne [version]$Version) {
    throw "whatsnew.json empieza por $primera y no por ${Version}: escribe antes las novedades (General 6.7)."
}

# 2. Banco de pruebas en verde (General 8.6).
dotnet build Scanner.slnx -c Debug -nologo -v q -m:1 -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'No compila.' }
dotnet test tests\Scanner.Tests --no-build -nologo
if ($LASTEXITCODE -ne 0) { throw 'El banco de pruebas no pasa.' }

# 3. Exe autocontenido de un solo fichero.
Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
dotnet publish src\Scanner.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -nologo -v q -m:1 -nodeReuse:false -o publish\app
if ($LASTEXITCODE -ne 0) { throw 'No se publica la aplicación.' }
$exe = 'publish\app\sOCScanner.exe'
$v = (Get-Item $exe).VersionInfo.FileVersion
if ([version]$v -ne [version]$Version) { throw "El exe publicado dice $v y no $Version." }
"Exe: $(Resolve-Path $exe) ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)"
if ($SoloCompilar) { return }

# 4. OneDrive (General 8): el exe y el LEEME. Si estaba abierta desde ahí, se cierra para sustituirla.
$d = 'C:\ID\OneDrive\Scanner'
New-Item -ItemType Directory -Force $d | Out-Null
$abierta = Get-Process sOCScanner -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$d*" }
$abierta | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item $exe $d -Force
$leeme = @"
sOC Scanner
===========

Versión $Version

Escanea con un solo botón (o Intro) con cualquier escáner que reconozca Windows (USB o red, cristal o
alimentador) o con una cámara. Una a una, con vista previa, girar y ordenar hojas, o en serie: cada
hoja se guarda sola con nombre correlativo. PDF, JPG, PNG, TIFF o BMP.

- sOCScanner.exe: la aplicación. Ábrela con doble clic; no hay que instalar nada.

Windows puede avisar de que el editor es desconocido ("Windows protegió su PC"): pulsa
"Más información" y "Ejecutar de todas formas".

Software libre bajo licencia MIT. En español y en inglés, claro y oscuro.
"@
[IO.File]::WriteAllText("$d\LEEME.txt", $leeme, (New-Object Text.UTF8Encoding $false))
"OneDrive: $d"

# 5. Git y release (General 8). Sin Co-Authored-By (General 3.5).
if (-not $SinGit) {
    if (-not $Mensaje) { throw 'Falta -Mensaje para el commit.' }
    git add -A
    git commit -q -m $Mensaje
    git push -q origin HEAD 2>&1 | Select-Object -Last 1
    python 'D:\sOCProjects\Mobile\Shared\release-github.py' "v$Version" $exe 2>&1 | Select-Object -Last 1
}

# 6. La entrega vuelve a dejar la aplicación abierta si lo estaba (General 8.3).
if ($abierta) {
    Start-Process "$d\sOCScanner.exe"
    Start-Sleep -Seconds 3
    $nueva = Get-Process sOCScanner -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$d*" } | Select-Object -First 1
    if (-not $nueva -or [version]$nueva.MainModule.FileVersionInfo.FileVersion -ne [version]$Version) { Write-Warning 'La versión nueva no ha quedado abierta.' }
}

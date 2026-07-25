param(
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Text)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

Set-Location -LiteralPath $RepoRoot
$validationErrors = [System.Collections.Generic.List[string]]::new()

Write-Step "Validando archivos requeridos"

$required = @(
    "compose.practico4.yaml"
    "src\BuildingBlocks\FinBank.IntegrationEvents\FinBank.IntegrationEvents.csproj"
    "src\BuildingBlocks\FinBank.RabbitMqResilience\FinBank.RabbitMqResilience.csproj"
    "src\TransfersService\Messaging\AccountTransferResultConsumer.cs"
    "src\NotificationsService\Messaging\TransferCompletedConsumer.cs"
    "src\ModularBank\Messaging\AccountsTransferRequestedConsumer.cs"
    "src\ModularBank\Messaging\AuditTransferResultConsumer.cs"
)

foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath $relativePath)) {
        $validationErrors.Add(("Falta archivo: {0}" -f $relativePath))
    }
}

Write-Step "Validando JSON Schema y ejemplos"

$schemas = @()
$examples = @()

if (Test-Path -LiteralPath "contracts\json-schema") {
    $schemas = @(Get-ChildItem -LiteralPath "contracts\json-schema" -File -Filter "*.json")
}

if (Test-Path -LiteralPath "contracts\examples") {
    $examples = @(Get-ChildItem -LiteralPath "contracts\examples" -File -Filter "*.json")
}

if ($schemas.Count -ne 5) {
    $validationErrors.Add(("Se esperaban 5 schemas; existen {0}." -f $schemas.Count))
}

if ($examples.Count -ne 5) {
    $validationErrors.Add(("Se esperaban 5 ejemplos; existen {0}." -f $examples.Count))
}

$allJsonFiles = @($schemas + $examples)
foreach ($jsonFile in $allJsonFiles) {
    try {
        $jsonText = Get-Content -LiteralPath $jsonFile.FullName -Raw
        $jsonObject = $jsonText | ConvertFrom-Json

        if ($null -eq $jsonObject) {
            throw "JSON vacío."
        }

        Write-Host ("JSON OK: {0}" -f $jsonFile.Name) -ForegroundColor Green
    }
    catch {
        $validationErrors.Add(
            ("JSON inválido {0}: {1}" -f $jsonFile.FullName, $_.Exception.Message)
        )
    }
}

Write-Step "Validando referencias de proyecto"

$projects = @(
    "src\TransfersService\TransfersService.csproj"
    "src\NotificationsService\NotificationsService.csproj"
    "src\ModularBank\ModularBank.csproj"
)

$expectedReferences = @(
    "..\BuildingBlocks\FinBank.IntegrationEvents\FinBank.IntegrationEvents.csproj"
    "..\BuildingBlocks\FinBank.RabbitMqResilience\FinBank.RabbitMqResilience.csproj"
)

foreach ($project in $projects) {
    if (-not (Test-Path -LiteralPath $project)) {
        $validationErrors.Add(
            ("No se encontró el proyecto: {0}" -f $project)
        )
        continue
    }

    try {
        [xml]$projectXml = Get-Content -LiteralPath $project -Raw

        $projectNode = $projectXml.SelectSingleNode(
            "/*[local-name()='Project']"
        )

        if ($null -eq $projectNode) {
            throw "No contiene un nodo Project válido."
        }

        $referenceNodes = @(
            $projectXml.SelectNodes(
                "/*[local-name()='Project']" +
                "/*[local-name()='ItemGroup']" +
                "/*[local-name()='ProjectReference']"
            )
        )

        $references = [System.Collections.Generic.List[string]]::new()

        foreach ($referenceNode in $referenceNodes) {
            if ($referenceNode -is [System.Xml.XmlElement]) {
                $include = $referenceNode.GetAttribute("Include")

                if (-not [string]::IsNullOrWhiteSpace($include)) {
                    $references.Add($include)
                }
            }
        }

        foreach ($expectedReference in $expectedReferences) {
            if ($references -notcontains $expectedReference) {
                $validationErrors.Add(
                    (
                        "{0} no referencia {1}." -f
                        $project,
                        $expectedReference
                    )
                )
            }
        }
    }
    catch {
        $validationErrors.Add(
            (
                "No fue posible analizar {0}: {1}" -f
                $project,
                $_.Exception.Message
            )
        )
    }
}

Write-Step "Validando problemas históricos"

$sourceFiles = @(Get-ChildItem -LiteralPath "src" -Recurse -File -Filter "*.cs")
$requeueMatches = @(
    $sourceFiles | Select-String -Pattern "requeue\s*:\s*true"
)

if ($requeueMatches.Count -gt 0) {
    $requeueMatches |
        Select-Object Path, LineNumber, Line |
        Format-Table -AutoSize

    $validationErrors.Add(
        "Se detectó requeue:true; el retry debe ser limitado con colas TTL."
    )
}

$transferSourceFiles = @(
    Get-ChildItem -LiteralPath "src\TransfersService" -Recurse -File -Filter "*.cs"
)

$duplicatePostRoutes = @(
    $transferSourceFiles | Select-String -SimpleMatch 'MapPost("/")'
)

$duplicateGetRoutes = @(
    $transferSourceFiles | Select-String -SimpleMatch 'MapGet("/")'
)

$duplicateRoutes = @($duplicatePostRoutes + $duplicateGetRoutes)
if ($duplicateRoutes.Count -gt 0) {
    $duplicateRoutes |
        Select-Object Path, LineNumber, Line |
        Format-Table -AutoSize

    $validationErrors.Add(
        "Se detectaron rutas '/' dentro de MapGroup; riesgo de AmbiguousMatchException."
    )
}

$legacyEndpoint = "src\ModularBank\Modules\Accounts\Api\InternalTransfersEndpoints.cs"
if (Test-Path -LiteralPath $legacyEndpoint) {
    $validationErrors.Add("InternalTransfersEndpoints.cs volvió a aparecer.")
}

$programPath = "src\ModularBank\Program.cs"
if (Test-Path -LiteralPath $programPath) {
    $program = Get-Content -LiteralPath $programPath -Raw
    $registrationCount = [regex]::Matches(
        $program,
        "AddSagaMessaging\s*\("
    ).Count

    if ($registrationCount -ne 1) {
        $validationErrors.Add(
            ("AddSagaMessaging debe aparecer una vez; aparece {0}." -f $registrationCount)
        )
    }
}
else {
    $validationErrors.Add(("No se encontró {0}." -f $programPath))
}

Write-Step "Validando sintaxis de scripts PowerShell"

$scriptFiles = @(
    Get-ChildItem -LiteralPath "scripts\windows" -File -Filter "*practico4*.ps1"
)

foreach ($scriptFile in $scriptFiles) {
    $tokens = $null
    $parseErrors = $null

    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $scriptFile.FullName,
        [ref]$tokens,
        [ref]$parseErrors
    )

    $currentErrors = @($parseErrors)
    if ($currentErrors.Count -gt 0) {
        foreach ($parseError in $currentErrors) {
            $validationErrors.Add(
                (
                    "Error PowerShell {0}, línea {1}, columna {2}: {3}" -f
                    $scriptFile.Name,
                    $parseError.Extent.StartLineNumber,
                    $parseError.Extent.StartColumnNumber,
                    $parseError.Message
                )
            )
        }
    }
    else {
        Write-Host ("PowerShell OK: {0}" -f $scriptFile.Name) -ForegroundColor Green
    }
}

Write-Step "Validando Dockerfiles"

$dockerfiles = @(
    "src\TransfersService\Dockerfile"
    "src\NotificationsService\Dockerfile"
    "src\ModularBank\Dockerfile"
)

foreach ($dockerfile in $dockerfiles) {
    if (-not (Test-Path -LiteralPath $dockerfile)) {
        $validationErrors.Add(("No se encontró Dockerfile: {0}" -f $dockerfile))
        continue
    }

    $dockerfileContent = Get-Content -LiteralPath $dockerfile -Raw
    $hasIntegrationEvents = $dockerfileContent -match "FinBank\.IntegrationEvents\.csproj"
    $hasResilience = $dockerfileContent -match "FinBank\.RabbitMqResilience\.csproj"

    if (-not $hasIntegrationEvents -or -not $hasResilience) {
        $validationErrors.Add(
            ("Dockerfile sin COPY de BuildingBlocks: {0}" -f $dockerfile)
        )
    }
}

if ($validationErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "VALIDACIÓN ESTÁTICA FALLIDA" -ForegroundColor Red

    foreach ($validationError in $validationErrors) {
        Write-Host ("- {0}" -f $validationError) -ForegroundColor Red
    }

    throw ("Se encontraron {0} problemas." -f $validationErrors.Count)
}

Write-Host ""
Write-Host "VALIDACIÓN ESTÁTICA SATISFACTORIA" -ForegroundColor Green

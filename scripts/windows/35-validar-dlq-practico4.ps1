param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$RabbitApi = "http://localhost:15672",
    [string]$RabbitUser = "finbank",
    [string]$RabbitPassword = "finbank-local",
    [int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-AuthHeader {
    $pair = "{0}:{1}" -f $RabbitUser, $RabbitPassword
    $bytes = [Text.Encoding]::ASCII.GetBytes($pair)
    $encoded = [Convert]::ToBase64String($bytes)

    return @{
        Authorization = "Basic {0}" -f $encoded
    }
}

function Get-Queue {
    param([string]$QueueName)

    $encodedQueue = [uri]::EscapeDataString($QueueName)
    $uri = "{0}/api/queues/%2F/{1}" -f $RabbitApi, $encodedQueue

    return Invoke-RestMethod `
        -Uri $uri `
        -Headers (Get-AuthHeader) `
        -Method Get `
        -TimeoutSec 20
}

function Publish-Message {
    param(
        [string]$RoutingKey,
        [string]$Payload
    )

    $bodyObject = @{
        properties = @{
            delivery_mode = 2
            content_type = "application/cloudevents+json; charset=utf-8"
            message_id = [Guid]::NewGuid().ToString("N")
        }
        routing_key = $RoutingKey
        payload = $Payload
        payload_encoding = "string"
    }

    $body = $bodyObject | ConvertTo-Json -Depth 10
    $uri = "{0}/api/exchanges/%2F/finbank.events/publish" -f $RabbitApi

    $response = Invoke-RestMethod `
        -Uri $uri `
        -Headers (Get-AuthHeader) `
        -Method Post `
        -ContentType "application/json" `
        -Body $body `
        -TimeoutSec 20

    if (-not $response.routed) {
        throw "RabbitMQ no enrutó el mensaje de prueba."
    }
}

Set-Location -LiteralPath $RepoRoot

$folderName = "evidencias\practico4-dlq-{0}" -f (
    Get-Date -Format "yyyyMMdd-HHmmss"
)
$evidenceDirectory = Join-Path $RepoRoot $folderName
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null

$dlqName = "accounts.transfer-requested.v1.dlq"
$before = Get-Queue -QueueName $dlqName

Write-Host ("DLQ antes: messages={0}" -f $before.messages)

$invalidPayload = '{"specversion":"1.0","id":'
Publish-Message `
    -RoutingKey "transfers.requested.v1" `
    -Payload $invalidPayload

$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
$after = $null

while ([DateTimeOffset]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $after = Get-Queue -QueueName $dlqName

    Write-Host ("DLQ: messages={0}" -f $after.messages)

    $expectedCount = [int]$before.messages + 1
    if ([int]$after.messages -ge $expectedCount) {
        break
    }
}

$minimumMessages = [int]$before.messages + 1
if ($null -eq $after -or [int]$after.messages -lt $minimumMessages) {
    throw "El mensaje inválido no llegó a la DLQ."
}

$result = @{
    result = "PASS"
    queue = $dlqName
    messagesBefore = [int]$before.messages
    messagesAfter = [int]$after.messages
    validatedAt = [DateTimeOffset]::Now
}

$result |
    ConvertTo-Json |
    Set-Content `
        -Path (Join-Path $evidenceDirectory "resultado.json") `
        -Encoding UTF8

Write-Host "VALIDACIÓN DLQ SATISFACTORIA" -ForegroundColor Green
Write-Host ("Evidencia: {0}" -f $evidenceDirectory) -ForegroundColor Cyan

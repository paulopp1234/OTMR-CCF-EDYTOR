param(
    [switch]$WriteOffsetCsv
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$hhdPath = Join-Path $repoRoot 'TestData\HHD_Serial_Trace_20260824_143201.txt'
$rawPath = Join-Path $repoRoot 'TestData\OTMR_RAW_CAPTURE_20260824.txt'
$ccfPath = Join-Path $repoRoot 'TestData\CLASS171_GUI_TEST.ccf'

function Convert-HexBytes([string]$hex) {
    return [byte[]]@($hex.Split(' ', [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { [Convert]::ToByte($_, 16) })
}

function Read-HhdWrites {
    $writes = [System.Collections.Generic.List[object]]::new()
    $current = $null
    foreach ($line in Get-Content -LiteralPath $hhdPath) {
        if ($line -match '^(\d+): Write Request \(DOWN\), ([^ ]+ [^ ]+)') {
            if ($null -ne $current) { $writes.Add([pscustomobject]$current) }
            $current = [ordered]@{
                Event = $matches[1]
                Timestamp = $matches[2]
                Bytes = [System.Collections.Generic.List[byte]]::new()
            }
            continue
        }
        if ($null -ne $current -and $line -match '^\s(?<hex>[0-9A-F]{2}(?: [0-9A-F]{2}){0,15})\s{2,}') {
            foreach ($value in $matches['hex'].Split(' ')) {
                $current.Bytes.Add([Convert]::ToByte($value, 16))
            }
            continue
        }
        if ($null -ne $current -and $line.Length -eq 0) {
            $writes.Add([pscustomobject]$current)
            $current = $null
        }
    }
    if ($null -ne $current) { $writes.Add([pscustomobject]$current) }
    return $writes
}

function Read-RawFrames {
    $buffer = [System.Collections.Generic.List[byte]]::new()
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($line in Get-Content -LiteralPath $rawPath) {
        $parts = $line -split '  +', 4
        if ($parts.Count -lt 3 -or $parts[1] -ne 'RX' -or
            -not $parts[0].StartsWith('2026-08-24T14:32:', [StringComparison]::Ordinal)) {
            continue
        }
        foreach ($value in (Convert-HexBytes $parts[2])) { $buffer.Add($value) }
        while ($true) {
            $start = $buffer.IndexOf(0x01)
            if ($start -lt 0) { $buffer.Clear(); break }
            if ($start -gt 0) { $buffer.RemoveRange(0, $start) }
            if ($buffer.Count -lt 9) { break }
            $length = if ($buffer[5] -eq 1 -and $buffer[6] -eq 1 -and $buffer[8] -eq 2) {
                12 + $buffer[7]
            } else { 13 }
            if ($buffer.Count -lt $length) { break }
            $frame = [byte[]]$buffer.GetRange(0, $length).ToArray()
            $frames.Add($frame)
            $buffer.RemoveRange(0, $length)
        }
    }
    return $frames
}

function Get-Payload([byte[]]$frame) {
    $length = $frame.Length - 12
    if ($length -le 0) { return [byte[]]@() }
    return [byte[]]$frame[9..($frame.Length - 4)]
}

function Get-Xor([byte[]]$bytes) {
    [byte]$value = 0
    foreach ($item in $bytes) { $value = $value -bxor $item }
    return $value
}

function Get-Sum([byte[]]$bytes) {
    [int]$value = 0
    foreach ($item in $bytes) { $value = ($value + $item) -band 0xFF }
    return [byte]$value
}

function Find-Sequence([byte[]]$haystack, [byte[]]$needle) {
    if ($needle.Length -eq 0 -or $needle.Length -gt $haystack.Length) { return -1 }
    for ($start = 0; $start -le $haystack.Length - $needle.Length; $start++) {
        $matches = $true
        for ($offset = 0; $offset -lt $needle.Length; $offset++) {
            if ($haystack[$start + $offset] -ne $needle[$offset]) { $matches = $false; break }
        }
        if ($matches) { return $start }
    }
    return -1
}

function Find-LongestCcfRun([byte[]]$ccfBytes, [byte[]]$payload) {
    $bestLength = 0
    $bestPayloadOffset = -1
    $bestCcfOffset = -1
    for ($payloadOffset = 0; $payloadOffset -lt $payload.Length; $payloadOffset++) {
        if ($payload[$payloadOffset] -eq 0) { continue }
        $searchFrom = 0
        while ($searchFrom -lt $ccfBytes.Length) {
            $ccfOffset = [Array]::IndexOf($ccfBytes, [byte]$payload[$payloadOffset], $searchFrom)
            if ($ccfOffset -lt 0) { break }
            $length = 0
            while ($payloadOffset + $length -lt $payload.Length -and
                   $ccfOffset + $length -lt $ccfBytes.Length -and
                   $payload[$payloadOffset + $length] -eq $ccfBytes[$ccfOffset + $length]) {
                $length++
            }
            if ($length -gt $bestLength) {
                $bestLength = $length
                $bestPayloadOffset = $payloadOffset
                $bestCcfOffset = $ccfOffset
            }
            $searchFrom = $ccfOffset + 1
        }
    }
    return [pscustomobject]@{ Length=$bestLength; PayloadOffset=$bestPayloadOffset; CcfOffset=$bestCcfOffset }
}

function Get-LcsMap([byte[]]$source, [byte[]]$target) {
    $sourceLength = $source.Length
    $targetLength = $target.Length
    $width = $targetLength + 1
    $scores = New-Object 'int[]' (($sourceLength + 1) * ($targetLength + 1))
    for ($sourceOffset = $sourceLength - 1; $sourceOffset -ge 0; $sourceOffset--) {
        for ($targetOffset = $targetLength - 1; $targetOffset -ge 0; $targetOffset--) {
            $index = ($sourceOffset * $width) + $targetOffset
            if ($source[$sourceOffset] -eq $target[$targetOffset]) {
                $scores[$index] = 1 + $scores[(($sourceOffset + 1) * $width) + $targetOffset + 1]
            }
            else {
                $skipSource = $scores[(($sourceOffset + 1) * $width) + $targetOffset]
                $skipTarget = $scores[($sourceOffset * $width) + $targetOffset + 1]
                $scores[$index] = [Math]::Max($skipSource, $skipTarget)
            }
        }
    }
    $map = @{}
    $sourceOffset = 0
    $targetOffset = 0
    while ($sourceOffset -lt $sourceLength -and $targetOffset -lt $targetLength) {
        if ($source[$sourceOffset] -eq $target[$targetOffset]) {
            $map[$targetOffset] = $sourceOffset
            $sourceOffset++
            $targetOffset++
        }
        elseif ($scores[(($sourceOffset + 1) * $width) + $targetOffset] -ge
                $scores[($sourceOffset * $width) + $targetOffset + 1]) {
            $sourceOffset++
        }
        else {
            $targetOffset++
        }
    }
    return $map
}

function Format-Bytes([byte[]]$bytes, [int]$start, [int]$length) {
    if ($length -le 0) { return '-' }
    return (($bytes[$start..($start + $length - 1)] | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')
}

function Get-EditGaps([byte[]]$source, [byte[]]$target, [hashtable]$map) {
    $gaps = [System.Collections.Generic.List[string]]::new()
    $sourceCursor = 0
    $targetCursor = 0
    foreach ($targetMatch in @($map.Keys | Sort-Object { [int]$_ })) {
        $targetMatch = [int]$targetMatch
        $sourceMatch = [int]$map[$targetMatch]
        if ($sourceMatch -gt $sourceCursor -or $targetMatch -gt $targetCursor) {
            $sourceLength = $sourceMatch - $sourceCursor
            $targetLength = $targetMatch - $targetCursor
            $gaps.Add(('RX payload 0x{0:X2}+{1} [{2}] -> TX payload 0x{3:X2}+{4} [{5}]' -f
                $sourceCursor, $sourceLength, (Format-Bytes $source $sourceCursor $sourceLength),
                $targetCursor, $targetLength, (Format-Bytes $target $targetCursor $targetLength)))
        }
        $sourceCursor = $sourceMatch + 1
        $targetCursor = $targetMatch + 1
    }
    if ($sourceCursor -lt $source.Length -or $targetCursor -lt $target.Length) {
        $sourceLength = $source.Length - $sourceCursor
        $targetLength = $target.Length - $targetCursor
        $gaps.Add(('RX payload 0x{0:X2}+{1} [{2}] -> TX payload 0x{3:X2}+{4} [{5}]' -f
            $sourceCursor, $sourceLength, (Format-Bytes $source $sourceCursor $sourceLength),
            $targetCursor, $targetLength, (Format-Bytes $target $targetCursor $targetLength)))
    }
    return $gaps
}

function Get-ModifiedMap([byte[]]$source, [byte[]]$target, [hashtable]$copyMap) {
    $modified = @{}
    $sourceCursor = 0
    $targetCursor = 0
    foreach ($targetMatch in @($copyMap.Keys | Sort-Object { [int]$_ })) {
        $targetMatch = [int]$targetMatch
        $sourceMatch = [int]$copyMap[$targetMatch]
        $sourceLength = $sourceMatch - $sourceCursor
        $targetLength = $targetMatch - $targetCursor
        if ($sourceLength -eq $targetLength) {
            for ($index = 0; $index -lt $targetLength; $index++) {
                $modified[$targetCursor + $index] = $sourceCursor + $index
            }
        }
        $sourceCursor = $sourceMatch + 1
        $targetCursor = $targetMatch + 1
    }
    $sourceLength = $source.Length - $sourceCursor
    $targetLength = $target.Length - $targetCursor
    if ($sourceLength -eq $targetLength) {
        for ($index = 0; $index -lt $targetLength; $index++) {
            $modified[$targetCursor + $index] = $sourceCursor + $index
        }
    }
    return $modified
}

function Get-Classification(
    [int]$offset,
    [byte[]]$rx,
    [byte[]]$tx,
    [hashtable]$copyMap,
    [hashtable]$modifiedMap,
    [bool]$isAcknowledgement) {
    if ($isAcknowledgement) {
        if ($offset -in 0, 8, 10, 12) { return 'protocol framing' }
        if ($offset -in 1, 9) { return 'command/index' }
        if ($offset -eq 11) { return 'checksum/CRC' }
        if ($offset -eq 5) { return 'copied from current OTMR' }
        return 'constant proven by multiple messages'
    }
    if ($offset -eq 0 -or $offset -eq 8 -or $offset -eq $tx.Length - 3 -or $offset -eq $tx.Length - 1) {
        return 'protocol framing'
    }
    if ($offset -eq 1 -or $offset -eq 3) {
        return 'command/index'
    }
    if ($offset -eq 7) { return 'length' }
    if ($offset -eq $tx.Length - 2) { return 'checksum/CRC' }
    if ($offset -eq 2) { return 'constant proven by multiple messages' }
    if ($copyMap.ContainsKey($offset)) { return 'copied from current OTMR' }
    if ($modifiedMap.ContainsKey($offset)) { return 'modified from current OTMR' }
    return 'unknown'
}

$writes = @(Read-HhdWrites)
$frames = @(Read-RawFrames)
$rxByTransaction = @{}
foreach ($frame in $frames) { $rxByTransaction[[int]$frame[1]] = $frame }
$blockedEvents = '000301', '000312', '000335', '000357', '000378', '000401', '000423'
$sources = 0x0C, 0x02, 0x04, 0x06, 0x08, 0x0A, 0x0C

Write-Output 'Payload check-byte candidates (bytes between STX 02 and ETX 03):'
foreach ($write in $writes) {
    [byte[]]$tx = $write.Bytes.ToArray()
    $payload = Get-Payload $tx
    '{0} len={1:X} observed={2:X2} xor={3:X2} sum={4:X2} negsum={5:X2}' -f
        $write.Event, $tx.Length, $tx[$tx.Length - 2], (Get-Xor $payload),
        (Get-Sum $payload), ((-(Get-Sum $payload)) -band 0xFF)
}

Write-Output 'Payload locations in CLASS171_GUI_TEST.ccf:'
[byte[]]$ccf = [IO.File]::ReadAllBytes($ccfPath)
foreach ($eventId in '000312', '000335', '000357', '000378', '000401', '000423') {
    [byte[]]$tx = ($writes | Where-Object Event -eq $eventId).Bytes.ToArray()
    [byte[]]$payload = Get-Payload $tx
    $location = Find-Sequence $ccf $payload
    $longest = Find-LongestCcfRun $ccf $payload
    '{0} payload={1} exact-ccf-offset={2}; longest={3} payload+0x{4:X2} -> ccf+0x{5:X4}' -f
        $eventId, $payload.Length,
        $(if ($location -ge 0) { '0x{0:X4}' -f $location } else { 'not found' }),
        $longest.Length, $longest.PayloadOffset, $longest.CcfOffset
}

Write-Output 'Hypothesized contiguous CCF slice comparison (base 0x01AF + page*0xFF):'
$pageNumber = 0
foreach ($eventId in '000312', '000335', '000357', '000378', '000401', '000423') {
    [byte[]]$tx = ($writes | Where-Object Event -eq $eventId).Bytes.ToArray()
    [byte[]]$payload = Get-Payload $tx
    $base = 0x01AF + ($pageNumber * 0xFF)
    $count = [Math]::Min($payload.Length, $ccf.Length - $base)
    $equal = 0
    $differentOffsets = [System.Collections.Generic.List[string]]::new()
    for ($offset = 0; $offset -lt $count; $offset++) {
        if ($payload[$offset] -eq $ccf[$base + $offset]) { $equal++ }
        elseif ($differentOffsets.Count -lt 12) {
            $differentOffsets.Add(('0x{0:X2}:{1:X2}>{2:X2}' -f $offset, $ccf[$base + $offset], $payload[$offset]))
        }
    }
    '{0} ccfBase=0x{1:X4}; equal={2}/{3}; firstDiffs={4}' -f
        $eventId, $base, $equal, $count, ($differentOffsets -join ', ')
    $pageNumber++
}

Write-Output 'RX payload to TX payload LCS coverage:'
for ($index = 1; $index -lt $blockedEvents.Count; $index++) {
    $eventId = $blockedEvents[$index]
    [byte[]]$rx = $rxByTransaction[$sources[$index]]
    [byte[]]$tx = ($writes | Where-Object Event -eq $eventId).Bytes.ToArray()
    [byte[]]$rxPayload = Get-Payload $rx
    [byte[]]$txPayload = Get-Payload $tx
    $map = Get-LcsMap $rxPayload $txPayload
    '{0} source=01 {1:X2}; rxPayload={2}; txPayload={3}; copied-LCS={4}; unexplained-TX={5}' -f
        $eventId, $sources[$index], $rxPayload.Length, $txPayload.Length,
        $map.Count, ($txPayload.Length - $map.Count)
    foreach ($gap in Get-EditGaps $rxPayload $txPayload $map) { "  $gap" }
}

$rows = [System.Collections.Generic.List[object]]::new()
for ($messageIndex = 0; $messageIndex -lt $blockedEvents.Count; $messageIndex++) {
    $eventId = $blockedEvents[$messageIndex]
    [byte[]]$rx = $rxByTransaction[$sources[$messageIndex]]
    [byte[]]$tx = ($writes | Where-Object Event -eq $eventId).Bytes.ToArray()
    $copyMap = @{}
    $modifiedMap = @{}
    $derivationMap = @{}
    if ($eventId -eq '000301') {
        $copyMap[5] = 4
        $derivationMap[1] = 1
        $derivationMap[5] = 4
        $derivationMap[9] = 1
    }
    else {
        [byte[]]$rxPayload = Get-Payload $rx
        [byte[]]$txPayload = Get-Payload $tx
        $payloadMap = Get-LcsMap $rxPayload $txPayload
        $payloadModifiedMap = Get-ModifiedMap $rxPayload $txPayload $payloadMap
        foreach ($targetPayloadOffset in $payloadMap.Keys) {
            $copyMap[[int]$targetPayloadOffset + 9] = [int]$payloadMap[$targetPayloadOffset] + 9
        }
        foreach ($targetPayloadOffset in $payloadModifiedMap.Keys) {
            $modifiedMap[[int]$targetPayloadOffset + 9] = [int]$payloadModifiedMap[$targetPayloadOffset] + 9
        }
        $derivationMap[1] = 3
        $derivationMap[3] = 3
        foreach ($offset in 4, 5, 6, 7) { $derivationMap[$offset] = $offset }
        foreach ($offset in $copyMap.Keys) { $derivationMap[$offset] = $copyMap[$offset] }
        foreach ($offset in $modifiedMap.Keys) { $derivationMap[$offset] = $modifiedMap[$offset] }
        foreach ($offset in 4, 5, 6) {
            if ($rx[$offset] -eq $tx[$offset]) { $copyMap[$offset] = $offset }
        }
    }
    for ($offset = 0; $offset -lt $tx.Length; $offset++) {
        $hasRx = $offset -lt $rx.Length
        $classification = Get-Classification $offset $rx $tx $copyMap $modifiedMap ($eventId -eq '000301')
        $derivedOffset = if ($derivationMap.ContainsKey($offset)) { [int]$derivationMap[$offset] } else { -1 }
        $rows.Add([pscustomobject]@{
            Event = $eventId
            SourceRx = ('01 {0:X2}' -f $sources[$messageIndex])
            OffsetHex = ('0x{0:X3}' -f $offset)
            Offset = $offset
            RxValue = if ($hasRx) { '{0:X2}' -f $rx[$offset] } else { '' }
            TxValue = '{0:X2}' -f $tx[$offset]
            Comparison = if (-not $hasRx) { 'no RX offset' } elseif ($rx[$offset] -eq $tx[$offset]) { 'unchanged' } else { 'changed' }
            Classification = $classification
            DerivedFromRxOffset = if ($derivedOffset -ge 0) { '0x{0:X3}' -f $derivedOffset } else { '' }
            DerivedRxValue = if ($derivedOffset -ge 0) { '{0:X2}' -f $rx[$derivedOffset] } else { '' }
        })
    }
}

if ($WriteOffsetCsv) {
    $output = Join-Path $repoRoot 'docs\CLASS171_OTMR_BLOCKED_WRITE_OFFSETS_20260824.csv'
    $rows | Export-Csv -LiteralPath $output -NoTypeInformation -Encoding utf8
    Write-Output "Wrote $output"
}

Write-Output 'TX-byte classification counts:'
$rows | Group-Object Event | ForEach-Object {
    $parts = $_.Group | Group-Object Classification | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }
    Write-Output "$($_.Name): $($parts -join '; ')"
}

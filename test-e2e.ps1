$ErrorActionPreference = 'Stop'
$OutputEncoding = [console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([Environment]::CurrentDirectory -ne $PSScriptRoot) { Set-Location $PSScriptRoot }
chcp 65001 | Out-Null

$base = 'http://localhost:5000'

# 1. login
$rc = Invoke-RestMethod -Method Post -Uri "$base/api/auth/request-code" -ContentType 'application/json' -Body '{"phone":"+380991112233","channel":"Sms","contact":null}'
$code = $rc.devCode
$auth = Invoke-RestMethod -Method Post -Uri "$base/api/auth/verify-code" -ContentType 'application/json' -Body ('{"phone":"+380991112233","code":"' + $code + '","name":"Taras","contact":null}')
$h = @{ Authorization = "Bearer $($auth.token)" }
Write-Output "LOGIN OK user=$($auth.user.name)"

# 2. slots
$slots = Invoke-RestMethod -Method Post -Uri "$base/api/booking/slots" -ContentType 'application/json' -Body '{"masterId":1,"serviceId":1}'
Write-Output "SLOTS count=$($slots.Count) first=$($slots[0].start)"

# 3. create booking (беремо перший вільний слот)
$bk = Invoke-RestMethod -Method Post -Uri "$base/api/booking/create" -Headers $h -ContentType 'application/json' -Body ('{"masterId":1,"serviceId":1,"startTime":"' + $slots[0].start + '","channel":"Telegram","contact":"12345"}')
Write-Output "BOOKING id=$($bk.appointmentId) devCode=$($bk.devCode) mock=$($bk.mock)"

# 4. confirm
$b3 = '{"appointmentId":' + $bk.appointmentId + ',"code":"' + $bk.devCode + '"}'
$cf = Invoke-RestMethod -Method Post -Uri "$base/api/booking/confirm" -Headers $h -ContentType 'application/json' -Body $b3
Write-Output "CONFIRM ok=$($cf.ok)"

# 5. history
$ap = Invoke-RestMethod -Uri "$base/api/account/appointments" -Headers $h
Write-Output "HISTORY $($ap | ConvertTo-Json -Compress)"

# 6. admin login + appointments
$ad = Invoke-RestMethod -Method Post -Uri "$base/api/auth/admin-login" -ContentType 'application/json' -Body '{"phone":"+380000000000","password":"admin123"}'
$ah = @{ Authorization = "Bearer $($ad.token)" }
$la = Invoke-RestMethod -Uri "$base/api/admin/appointments" -Headers $ah
Write-Output "ADMIN appointments count=$($la.Count)"

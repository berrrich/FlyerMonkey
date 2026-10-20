param(
    [Parameter(Mandatory)][string]$FixturePath,
    [string]$BrowserPath = 'C:\Program Files\Google\Chrome\Application\chrome.exe',
    [int]$Width = 412,
    [int]$Height = 915,
    [switch]$ReducedMotion
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('FlyerMonkey-Comet-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$css = @(
    'FlyerMonkey/FlyerMonkey.Shared/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css',
    'FlyerMonkey/FlyerMonkey.Shared/Layout/MainLayout.razor.css',
    'FlyerMonkey/FlyerMonkey.Shared/Pages/ProductDetail.razor.css'
) | ForEach-Object { [IO.File]::ReadAllText((Join-Path $repoRoot $_)).Replace('::deep', '') }
$module = [IO.File]::ReadAllText((Join-Path $repoRoot 'FlyerMonkey/FlyerMonkey.Shared/wwwroot/craterInvitation.js'))
$product = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $FixturePath))
# Replace image URLs only in the offline fixture; never contact an API.
$product = [regex]::Replace($product, 'src="[^"]*"', 'src="data:image/svg+xml,%3Csvg xmlns=%27http://www.w3.org/2000/svg%27 width=%27350%27 height=%27350%27/%3E"')
$nav = '<nav class="fm-bottom-nav" aria-label="Mobile navigation"><a href="#"><span>&#127991;</span><span>Offers</span></a><a href="#"><span>&#128344;</span><span>Previous</span></a><a href="#"><span>&#128722;</span><span>Products</span></a></nav>'
$checks = @'
const report = document.querySelector('#test-result');
const check = (condition, message) => { if (!condition) throw new Error(message); };
try {
    globalThis.fetch = () => { throw new Error('Network forbidden'); };
    const comet = document.querySelector('.fm-floating-comet');
    const dialog = document.querySelector('dialog');
    const controls = document.querySelector('#crater-controls');
    const closed = () => new Promise(resolve => dialog.addEventListener('close', resolve, { once: true }));
    const [yes, no] = dialog.querySelectorAll('button');
    comet.onclick = () => openInvitation(dialog, comet, controls);
    yes.onclick = () => closeInvitation(dialog, true);
    no.onclick = () => closeInvitation(dialog, false);
    check(!dialog.open, 'Invitation must not open automatically');
    check(document.querySelectorAll('#crater-style option').length === 5, 'All five presets');
    check(document.querySelectorAll('#crater-controls').length === 1, 'One control section');
    const floatingIcon = comet.querySelector('svg');
    const controlIcon = controls.querySelector('button svg');
    check(floatingIcon && controlIcon && floatingIcon.innerHTML === controlIcon.innerHTML, 'Floating comet uses the same vector and colours');
    check(getComputedStyle(comet).backgroundColor === 'rgb(255, 255, 255)', 'White circular surround');
    check(document.documentElement.scrollWidth <= innerWidth, 'No horizontal overflow');
    const floating = comet.getBoundingClientRect();
    const column = document.querySelector('.fm-creative-column').getBoundingClientRect();
    check(column.right < floating.left, 'Button must not cover content');
    const nav = document.querySelector('.fm-bottom-nav');
    if (getComputedStyle(nav).display !== 'none')
        check(floating.bottom < nav.getBoundingClientRect().top, 'Button must clear bottom navigation');
    const animation = getComputedStyle(comet.querySelector('span')).animationName;
    check(matchMedia('(prefers-reduced-motion: reduce)').matches ? animation === 'none' : animation !== 'none', 'Reduced motion');
    comet.focus(); comet.click();
    check(dialog.open && dialog.matches(':modal'), 'Native modal opens');
    check(document.activeElement === no, 'Safe initial focus');
    controls.focus();
    check(dialog.contains(document.activeElement), 'Background cannot take modal focus');
    const bounds = dialog.getBoundingClientRect();
    check(bounds.left >= 0 && bounds.right <= innerWidth && bounds.top >= 0 && bounds.bottom <= innerHeight, 'Dialog fits viewport');
    let closing = closed(); no.click(); await closing;
    check(!dialog.open && document.activeElement === comet, 'No dismisses and restores focus');
    comet.click();
    closing = closed(); dialog.requestClose(); await closing;
    check(!dialog.open && document.activeElement === comet, 'Native close request restores focus');
    comet.click(); closing = closed(); yes.click(); await closing;
    check(!dialog.open && document.activeElement === controls, 'Yes reveals and focuses existing controls');
    check(document.querySelectorAll('#crater-controls').length === 1, 'No duplicate controls after acceptance');
    // A queued open after navigation must not reopen a detached dialog.
    const detached = dialog.cloneNode(true);
    openInvitation(detached, comet, controls);
    check(!detached.open, 'Detached dialog cannot reopen');
    scrollTo({ top: 0, behavior: 'instant' });
    report.textContent = `PASS ${innerWidth}x${innerHeight}; reducedMotion=${matchMedia('(prefers-reduced-motion: reduce)').matches}; layout, navigation clearance, dialog focus, dismiss, accept, detached guard`;
} catch (error) { report.textContent = 'FAIL ' + error.message; }
'@
$html = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>' + ($css -join "`n") + '</style></head><body><main><article class="content px-4">' + $product + '</article></main>' + $nav + '<pre id="test-result" hidden>NOT RUN</pre><script type="module">' + $module + "`n" + $checks + '</script></body></html>'
$htmlPath = Join-Path $testRoot 'fixture.html'
[IO.File]::WriteAllText($htmlPath, $html, [Text.UTF8Encoding]::new($false))
$output = Join-Path $testRoot 'dom.html'
$arguments = @('--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--remote-debugging-port=0',
    "--user-data-dir=`"$(Join-Path $testRoot 'profile')`"", "--window-size=$Width,$Height",
    '--host-resolver-rules="MAP * ~NOTFOUND"', 'about:blank')
$process = Start-Process -FilePath $BrowserPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $output -RedirectStandardError (Join-Path $testRoot 'browser.log')
$socket = [Net.WebSockets.ClientWebSocket]::new()
$script:cdpId = 0
function Invoke-Cdp([string]$Method, [hashtable]$Parameters = @{}) {
    $script:cdpId++
    $message = @{ id = $script:cdpId; method = $Method; params = $Parameters } | ConvertTo-Json -Depth 20 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($message)
    $socket.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    do {
        $stream = [IO.MemoryStream]::new()
        do {
            $buffer = New-Object byte[] 65536
            $received = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), [Threading.CancellationToken]::None).GetAwaiter().GetResult()
            $stream.Write($buffer, 0, $received.Count)
        } while (!$received.EndOfMessage)
        $response = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
        $stream.Dispose()
    } while ($response.id -ne $script:cdpId)
    if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
    return $response.result
}
try {
    $portFile = Join-Path $testRoot 'profile/DevToolsActivePort'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (!(Test-Path -LiteralPath $portFile)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Browser did not start' }
        Start-Sleep -Milliseconds 100
    }
    $port = [IO.File]::ReadAllLines($portFile)[0]
    $targets = Invoke-RestMethod "http://localhost:$port/json"
    $target = $targets | Where-Object type -eq 'page' | Select-Object -First 1
    $socket.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
    Invoke-Cdp 'Emulation.setDeviceMetricsOverride' @{ width = $Width; height = $Height; deviceScaleFactor = 1; mobile = ($Width -le 640) } | Out-Null
    $motion = if ($ReducedMotion) { 'reduce' } else { 'no-preference' }
    Invoke-Cdp 'Emulation.setEmulatedMedia' @{ features = @(@{ name = 'prefers-reduced-motion'; value = $motion }) } | Out-Null
    Invoke-Cdp 'Page.navigate' @{ url = ([Uri]$htmlPath).AbsoluteUri } | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $result = (Invoke-Cdp 'Runtime.evaluate' @{ expression = "document.querySelector('#test-result')?.textContent"; returnByValue = $true }).result.value
    } while (!$result -or ($result -eq 'NOT RUN' -and [DateTime]::UtcNow -lt $deadline))
    if (!$result.StartsWith("PASS ${Width}x${Height};")) { throw "Browser checks failed: $result" }
    # Use trusted browser keyboard events, not synthetic DOM key events.
    Invoke-Cdp 'Runtime.evaluate' @{ expression = "document.querySelector('.fm-floating-comet').click()" } | Out-Null
    foreach ($key in @('Tab', 'Tab', 'Tab', 'Tab')) {
        Invoke-Cdp 'Input.dispatchKeyEvent' @{ type = 'keyDown'; key = $key; code = $key; windowsVirtualKeyCode = 9 } | Out-Null
        Invoke-Cdp 'Input.dispatchKeyEvent' @{ type = 'keyUp'; key = $key; code = $key; windowsVirtualKeyCode = 9 } | Out-Null
        $trapped = (Invoke-Cdp 'Runtime.evaluate' @{ expression = "document.querySelector('dialog').contains(document.activeElement) || document.activeElement === document.body"; returnByValue = $true }).result.value
        if (!$trapped) { throw 'Tab escaped the modal' }
    }
    Invoke-Cdp 'Runtime.evaluate' @{ expression = "globalThis.closedByEscape = new Promise(resolve => document.querySelector('dialog').addEventListener('close', resolve, { once: true }))" } | Out-Null
    Invoke-Cdp 'Input.dispatchKeyEvent' @{ type = 'keyDown'; key = 'Escape'; code = 'Escape'; windowsVirtualKeyCode = 27 } | Out-Null
    Invoke-Cdp 'Input.dispatchKeyEvent' @{ type = 'keyUp'; key = 'Escape'; code = 'Escape'; windowsVirtualKeyCode = 27 } | Out-Null
    $escaped = (Invoke-Cdp 'Runtime.evaluate' @{ expression = "Promise.race([closedByEscape, new Promise(resolve => setTimeout(resolve, 1000))]).then(() => !document.querySelector('dialog').open && document.activeElement === document.querySelector('.fm-floating-comet'))"; awaitPromise = $true; returnByValue = $true }).result.value
    if (!$escaped) { throw 'Escape did not close and restore focus' }
    $screenshot = Invoke-Cdp 'Page.captureScreenshot' @{ format = 'png' }
    [IO.File]::WriteAllBytes((Join-Path $testRoot 'layout.png'), [Convert]::FromBase64String($screenshot.data))
    Write-Output "$result; trusted Tab/Escape passed"
    Write-Output "Artifacts: $testRoot"
} finally {
    $socket.Dispose()
    # Stop only this test's isolated browser, never an existing user browser.
    if (!$process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
}

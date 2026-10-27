param([Parameter(Mandatory = $true)][string]$CompilerDll)

$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $CompilerDll).Path
$probeDir = Join-Path ([IO.Path]::GetTempPath()) ('sage-http-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir | Out-Null
$stdNames = @('http_client.sg', 'http_server.sg', 'text.sg', 'console.sg')
$cases = @(
    @{ Name = 'get_requires_text'; Code = 'use http_client; use text; func main(): none { var body: Text = text::create(8); http_client::get("http://x", &mut body); }'; Error = 'S170' },
    @{ Name = 'response_requires_mutable_borrow'; Code = 'use http_client; use text; func main(): none { var url: Text = text::from("http://x"); var body: Text = text::create(8); http_client::get(&url, &body); }'; Error = 'S170' },
    @{ Name = 'url_response_alias'; Code = 'use http_client; use text; func main(): none { var url: Text = text::from("http://x"); http_client::get(&url, &mut url); }'; Error = 'S172' },
    @{ Name = 'shell_post_requires_unsafe'; Code = 'use http_client; func main(): none { http_client::post_shell("http://x", "d"); }'; Error = 'S164' },
    @{ Name = 'shell_download_requires_unsafe'; Code = 'use http_client; func main(): none { http_client::download_shell("http://x", "p"); }'; Error = 'S164' },
    @{ Name = 'legacy_serve_requires_unsafe'; Code = 'use http_server; func main(): none { http_server::serve(8080, "x"); }'; Error = 'S164' },
    @{ Name = 'listener_use_after_close'; Code = 'use http_server; func main(): none { var s: HttpListener = http_server::listen_local(0); http_server::close(s); s.port(); }'; Error = 'S140' },
    @{ Name = 'listener_close_as_method'; Code = 'use http_server; func main(): none { var s: HttpListener = http_server::listen_local(0); s.close(); }'; Error = 'S159' },
    @{ Name = 'shared_listener_accept'; Code = 'use http_server; use text; func serve(s: &HttpListener, r: &mut Text): none { s.accept(&mut r); } func main(): none {}'; Error = 'S171' },
    @{ Name = 'listener_native_requires_unsafe'; Code = 'use http_server; func main(): none { http_server_native::sage_http_listen(0, 1); }'; Error = 'S164' }
)

function New-Project([string]$name, [string]$fixture, [string]$port) {
    $dir = Join-Path $probeDir $name
    New-Item -ItemType Directory -Path (Join-Path $dir 'src'), (Join-Path $dir 'std') | Out-Null
    foreach ($std in $stdNames) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$std") -Destination (Join-Path $dir 'std') }
    $code = (Get-Content -LiteralPath (Join-Path $PSScriptRoot "fixtures/$fixture") -Raw).Replace('@PORT@', $port)
    Set-Content -LiteralPath (Join-Path $dir 'src/main.sg') -Value $code
    Push-Location $dir
    try { $output = & dotnet $compilerPath build 2>&1 } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "$name build failed.`n$($output -join "`n")" }
    return Join-Path $dir "bin/$name.exe"
}

try {
    # 1. Exact argv transfer without a shell: a curl test double beside the program
    #    (Windows searches the application directory first) records what it receives.
    $argsExe = New-Project 'arguments' 'http-arguments.sg' '0'
    $fake = Join-Path (Split-Path -Parent $argsExe) 'curl.exe'
    $output = & gcc -std=c11 -Wall -Wextra -Werror (Join-Path $PSScriptRoot 'fixtures/fake-curl.c') -o $fake 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fake curl build failed.`n$($output -join "`n")" }
    $runDir = Join-Path $probeDir 'run'
    New-Item -ItemType Directory -Path (Join-Path $runDir 'out dir') | Out-Null
    Push-Location $runDir
    try { $actual = (& $argsExe) -join "`n"; $status = $LASTEXITCODE } finally { Pop-Location }
    if ($status -ne 0) { throw "Argument program failed.`n$actual" }
    $expected = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/http-arguments.expected')) -join "`n"
    if ($actual.Trim() -ne $expected.Trim()) { throw "Unexpected curl arguments.`n--- actual ---`n$actual`n--- expected ---`n$expected" }
    $downloaded = (Get-Content -LiteralPath (Join-Path $runDir 'out dir/file name.txt')) -join "`n"
    if (!$downloaded.Contains("<--output>`n<out dir\file name.txt>`n<http://example.invalid/a?x=`"q`"&echo&INJECTED|^%PATH%\>")) { throw "Unexpected download arguments.`n$downloaded" }

    # 2. Live loopback round trip: safe listener (Sage) <- WebClient and <- safe client (real curl).
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $probe.Start(); $port = ([Net.IPEndPoint]$probe.LocalEndpoint).Port; $probe.Stop()
    $serverExe = New-Project 'server' 'http-server.sg' "$port"
    $clientExe = New-Project 'client' 'http-client-live.sg' "$port"
    $serverOut = Join-Path $probeDir 'server.out'
    $server = Start-Process -FilePath $serverExe -RedirectStandardOutput $serverOut -PassThru -WindowStyle Hidden
    $null = $server.Handle # keeps the exit code readable after exit
    try {
        $web = New-Object Net.WebClient
        $first = $null
        for ($try = 0; $try -lt 50 -and $null -eq $first; $try++) {
            try { $first = $web.DownloadString("http://127.0.0.1:$port/probe?x=1") } catch { Start-Sleep -Milliseconds 200 }
        }
        if ($first -notmatch '^you sent: GET /probe\?x=1 HTTP/1\.1') { throw "Unexpected WebClient response: $first" }
        $clientOutput = (& $clientExe) -join "`n"
        if ($clientOutput -notmatch 'ok=1 exit=0 truncated=0' -or $clientOutput -notmatch 'you sent: GET /from-sage\?q=a&b=%22c%22 HTTP/1\.1') { throw "Unexpected client output.`n$clientOutput" }
        if (!$server.WaitForExit(20000)) { throw 'Server did not finish after two requests.' }
    } finally { if (!$server.HasExited) { $server.Kill() } }
    $serverText = (Get-Content -LiteralPath $serverOut) -join "`n"
    if ($server.ExitCode -ne 0 -or $serverText -ne "listening=1 port_matches=1 clash=0`nephemeral=1`nserved=2 respond_without_client=0") { throw "Unexpected server output.`n$serverText" }
    # The listener was closed: the port can be bound again immediately.
    $rebind = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    $rebind.Start(); $rebind.Stop()

    # 3. Rejected programs.
    $negative = Join-Path $probeDir 'negative'
    New-Item -ItemType Directory -Path (Join-Path $negative 'src'), (Join-Path $negative 'std') | Out-Null
    foreach ($std in $stdNames) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../Sandbox/std/$std") -Destination (Join-Path $negative 'std') }
    foreach ($case in $cases) {
        $source = Join-Path $negative "src/$($case.Name).sg"
        Set-Content -LiteralPath $source -Value $case.Code
        $output = & dotnet $compilerPath $source 2>&1
        if ($LASTEXITCODE -ne 1 -or ($output -join "`n") -notmatch ("error " + $case.Error + ':')) { throw "$($case.Name): expected $($case.Error).`n$($output -join "`n")" }
    }
    Write-Output "HTTP checks passed: argv transfer without shell, loopback server/client round trip, listener rebind, $($cases.Count) negative cases."
}
finally {
    $cleanupPath = [IO.Path]::GetFullPath($probeDir)
    if (!$cleanupPath.StartsWith((Join-Path ([IO.Path]::GetTempPath()) 'sage-http-'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force
}

# REQ-3 patch helper. Run from the extracted package, with Unity closed.
# It only adds a few snippets to four existing source files; no git/network commands.
$ErrorActionPreference = 'Stop'
$ProjectRoot = 'C:\stagereadyvr'
$Assets = Join-Path $ProjectRoot 'Assets'
$PackageRoot = $PSScriptRoot

if (-not (Test-Path (Join-Path $Assets 'Scripts\SessionManager.cs'))) { throw "Project not found at $ProjectRoot. Edit `$ProjectRoot in this script if needed." }

$specs = @(
    @{ File='Scripts\SessionManager.cs'; Find='        feedbackController?.EndSession();'; Mark='REQ3SessionScoringBridge.OnSessionEnded'; Add=@'
        if (feedbackController != null)
            StageReadyVR.PerformanceScoring.REQ3SessionScoringBridge.OnSessionEnded(
                feedbackController.CurrentReport, feedbackController);
'@ },
    @{ File='Scripts\Feedback\SessionReport.cs'; Find='        public SessionScores scores;'; Mark='req3AiScores'; Add=@'
        // REQ-3 AI scores are separate from the existing calculated scores.
        public StageReadyVR.PerformanceScoring.PerformanceScores req3AiScores;
        public string req3AiStatus = "not_requested";
        public string req3AiError = "";
'@ },
    @{ File='Scripts\Feedback\FeedbackController.cs'; Find='        public SessionReport CurrentReport => _report;'; Mark='public void ShowReq3Status('; Add=@'
        public void ShowReq3Status(SessionReport report)
        {
            if (report == _report && dashboard != null)
                dashboard.ShowReq3Status(report);
        }
'@ },
    @{ File='Scripts\Feedback\DashboardPanel.cs'; Find='        private static string BuildSummary(SessionReport r)'; Mark='public void ShowReq3Status('; Before=$true; Add=@'
        public void ShowReq3Status(SessionReport report)
        {
            if (report == null || summaryText == null) return;
            string status = report.req3AiStatus;
            string detail;
            if (status == "completed" && report.req3AiScores != null)
            {
                var s = report.req3AiScores;
                detail = $"GPT-6 Luna: Eye {s.eyeContact}/100 | Fillers {s.fillerWords}/100 | " +
                         $"Confidence {s.confidence}/100 | Pace {s.speechPacing}/100\n" +
                         (s.feedback ?? "").Replace("<", "‹").Replace(">", "›");
            }
            else if (status == "failed")
                detail = "AI scoring unavailable: " + (report.req3AiError ?? "Please try again.");
            else
                detail = "AI performance scoring in progress...";
            summaryText.text = BuildSummary(report) + "\n" + detail;
        }
'@ }
)

# Validate everything BEFORE writing anything to the Unity project.
$edits = @()
foreach ($spec in $specs) {
    $file = Join-Path $Assets $spec['File']
    if (-not (Test-Path $file)) { throw "Missing expected source file: $file. No changes made." }
    $contents = [System.IO.File]::ReadAllText($file)
    if ($contents.Contains($spec['Mark'])) {
        Write-Host "Already patched: $($spec['File'])"
        continue
    }
    if (-not $contents.Contains($spec['Find'])) { throw "Expected insertion point not found in $file. No changes made. Use PATCH_SNIPPETS.md." }
    $index = $contents.IndexOf($spec['Find'])
    if ($contents.IndexOf($spec['Find'], $index+1) -ge 0) { throw "Multiple insertion points in $file; stopping. No changes made." }
    $newline = if ($contents.Contains("`r`n")) { "`r`n" } else { "`n" }
    $insert = (($spec['Add'] -replace "(\r?\n)+$", "") -replace "\r\n|\r|\n", $newline)
    if ($spec['Before']) {
        $result = $contents.Insert($index, $insert + $newline)
    } else {
        $result = $contents.Insert($index + $spec['Find'].Length, $newline + $insert)
    }
    $edits += @{Path=$file; Content=$result}
}

$requiredScripts = @(
    'Assets\Scripts\PerformanceScoring\REQ3SessionScoringBridge.cs',
    'Assets\Editor\REQ3EditorSessionKeyWindow.cs',
    'Assets\Editor\REQ3FinalizedReportTest.cs'
)
foreach ($source in $requiredScripts) {
    if (-not (Test-Path (Join-Path $PackageRoot $source))) { throw "Package file missing: $source. No changes made." }
    $dest = Join-Path $ProjectRoot $source
    if (Test-Path $dest) { throw "Destination already exists: $dest. Inspect first; no changes made." }
}

$backupDir = Join-Path $env:TEMP ("StageReadyVR_REQ3_Backup_" + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
foreach ($entry in $edits) {
    $relative = $entry.Path.Substring($ProjectRoot.Length).TrimStart('\')
    $backupPath = Join-Path $backupDir $relative
    New-Item -ItemType Directory -Path (Split-Path $backupPath) -Force | Out-Null
    Copy-Item -LiteralPath $entry.Path -Destination $backupPath
}
# Copy files BEFORE changing existing sources; on any failure original team files are intact.
foreach ($source in $requiredScripts) {
    $dest = Join-Path $ProjectRoot $source
    New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PackageRoot $source) -Destination $dest
}
foreach ($entry in $edits) {
    # UTF8 encoding without BOM is recognized by Unity and preserves source characters.
    [System.IO.File]::WriteAllText($entry.Path, $entry.Content, (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host "Done. Patched $($edits.Count) source files and installed 3 new scripts." -ForegroundColor Green
Write-Host "Backups are stored at: $backupDir"
Write-Host "No git pull, stash, or commit was performed. Now reopen Unity."

$ErrorActionPreference = 'Stop'
foreach ($project in $args) {
    $raw = & dotnet list $project package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw "Dependency audit failed: $project" }
    $audit = $raw | ConvertFrom-Json
    if ($audit.problems) { throw "Dependency advisory service unavailable: $project" }
    foreach ($item in $audit.projects) {
        foreach ($framework in $item.frameworks) {
            if ($framework.topLevelPackages -or $framework.transitivePackages) { throw "Known vulnerable dependency: $project" }
        }
    }
}

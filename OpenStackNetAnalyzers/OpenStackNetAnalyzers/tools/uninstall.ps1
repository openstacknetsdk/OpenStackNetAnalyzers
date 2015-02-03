param($installPath, $toolsPath, $package, $project)

$analyzerPath = join-path $toolsPath "analyzers"
$analyzerFilePath = join-path $analyzerPath "OpenStackNetAnalyzers.dll"

$project.Object.AnalyzerReferences.Remove("$analyzerFilePath")
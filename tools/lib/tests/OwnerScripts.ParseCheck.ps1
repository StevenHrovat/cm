<#
Parse check of every owner script: the file parses, it uses no syntax Windows PowerShell 5.1 does not have, and no command argument is a "cast" that
PowerShell reads as plain text. Exit code 0 = clean. Run by tools/verify (step scripts) with powershell.exe where there is
one (that is the real 5.1 parser), with pwsh otherwise (then the second rule below catches what 5.1 would reject).

The cast rule is the DG1 failure of 2026-10-25: `Invoke-Command -ArgumentList $a, [bool]$b` hands the node the TEXT "[bool]False", not a boolean; write ([bool]$b).
#>
param([Parameter(Mandatory)][string[]]$Files)
$bad = 0
function Fail([string]$File, [int]$Line, [string]$Text) { $script:bad++; Write-Host ("FAIL {0}:{1} {2}" -f $File, $Line, $Text) }
foreach ($file in $Files) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
    foreach ($e in $errors) { Fail $file $e.Extent.StartLineNumber ("does not parse: " + $e.Message) }
    if ($errors.Count -gt 0) { continue }
    $newSyntax = 'QuestionQuestion', 'QuestionQuestionEquals', 'QuestionDot', 'QuestionLBracket', 'AndAnd', 'OrOr', 'Question'
    foreach ($t in $tokens) {
        if ($newSyntax -contains [string]$t.Kind) { Fail $file $t.Extent.StartLineNumber ("PowerShell 7 only syntax '" + $t.Text + "' (the scripts run in Windows PowerShell 5.1)") }
    }
    $newAst = 'TernaryExpressionAst', 'PipelineChainAst', 'NullConditionalMemberAccessAst'
    foreach ($n in $ast.FindAll({ param($a) $newAst -contains $a.GetType().Name }, $true)) { Fail $file $n.Extent.StartLineNumber ("PowerShell 7 only syntax (" + $n.GetType().Name + ")") }
    foreach ($c in $ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        for ($i = 1; $i -lt $c.CommandElements.Count; $i++) {
            $el = $c.CommandElements[$i]
            if ($el -is [System.Management.Automation.Language.StringConstantExpressionAst] -or $el -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
                if ($el.StringConstantType -eq 'BareWord' -and $el.Extent.Text -match '^\[[A-Za-z][A-Za-z0-9_.]*(\[\])?\]\$') {
                    Fail $file $el.Extent.StartLineNumber ("'" + $el.Extent.Text + "' is read as text, not as a cast. Write (" + $el.Extent.Text + ").")
                }
            }
        }
    }
    foreach ($a in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.ArrayLiteralAst] }, $true)) {
        foreach ($el in $a.Elements) {
            if ($el -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -and $el.StringConstantType -eq 'BareWord' -and $el.Extent.Text -match '^\[[A-Za-z][A-Za-z0-9_.]*(\[\])?\]\$') {
                Fail $file $el.Extent.StartLineNumber ("'" + $el.Extent.Text + "' is read as text, not as a cast. Write (" + $el.Extent.Text + ").")
            }
        }
    }
}
Write-Host ("{0} file(s) checked, {1} problem(s)" -f $Files.Count, $bad)
if ($bad -gt 0) { exit 1 }

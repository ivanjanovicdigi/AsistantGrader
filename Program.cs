// =====================================================================
//  AssistantGrader  -  "Asistent" assignment (3 phases, 40 points each)
//
//  1. Clones/updates student repos listed in repos.txt
//  2. Static analysis: checks the code structure (Phase 2 functions)
//  3. Builds each project and runs the functional scenarios
//  4. Computes rubric points per phase
//  5. Writes everything to results.csv (program output per student in /logs)
//
//  Run:  dotnet run              (uses ./submissions)
//        dotnet run -- C:\path\to\submissions
// =====================================================================


// ================= USINGS =================
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
// ==========================================


// ================= SETTINGS =================
var submissionsDir  = args.Length > 0 ? args[0] : "submissions";
var reposFile       = "repos.txt";   // format per line:  StudentName;https://gitlab.com/...
var resultsFile     = "results.csv";
var reportFile      = "report.html"; // every test per student: what was typed, what was printed
var logsDir         = "logs";
var scenarioTimeout = TimeSpan.FromSeconds(5);
var buildTimeout    = TimeSpan.FromMinutes(2);

// Missing or broken project file: the grader builds the student's .cs files with a standard
// project instead, grades them as usual, and takes this many points off the total.
var setupPenalty    = 10;
// ============================================


// ================= FUNCTIONAL: SCENARIOS =================
// new(phase, command, column name, inputs typed in order, check, Edge: true for edge cases)
//
// Checks:
//   Has("35")              output contains this number, or this word
//   HasAny("3", "2")       at least one of them
//   HasAll("sum", "odm")   all of them
//   Sequence("1 4 5 7")    these numbers in this order, one after another
//   NoCrash("NaN")         program ends normally; optional numbers/words that must NOT appear
//   Found("8", "9")        a position (one of these) or a "found" style answer (nachádza, true, áno...)
//   NotFound()             -1 or a "not found" style answer (nenachádza, false, nie...)
//   AllOf(check1, check2)  all of these checks must pass
//   RejectsText("40","42") for text typed instead of a number: no crash, no 0, and not "40" without "42"
//
// Numbers are compared at the precision the student printed: expected 8.6023 accepts
// 8.6, 8,60 or 8.602325..., but not 9. A whole number like 35 must be exact (35 or 35.0).
// Words ignore case and diacritics and match the start of a word
// ("nachadz" matches "nachádza", but not "nenachádza"). They also survive Slovak letters that
// Windows garbled on the way from the student's program ("nach dza", "nach├ídza").
//
// Always end the inputs with "exit" (twice inside rect/array mode: the first may only leave the mode).
// Inputs are whole numbers on purpose: "3.14" crashes
// double.Parse on Slovak Windows settings, and the assignment says whole numbers are enough.
var scenarios = new List<Scenario>
{
    // ---------- PHASE 1: sum, odm, find, help ----------
    // help must print the command names more often than an unknown command does,
    // so a menu shown anyway doesn't count as a working help
    new(1, "help", "help lists commands", ["help", "exit"],                HelpLists("sum", "odm", "find")),

    new(1, "sum",  "sum 522+3",           ["sum", "522", "3", "exit"],     Has("525")),
    new(1, "sum",  "sum -5+2",            ["sum", "-5", "2", "exit"],      Has("-3")),

    new(1, "odm",  "odm 16",              ["odm", "16", "exit"],           Has("4")),
    new(1, "odm",  "odm 2",               ["odm", "2", "exit"],            Has("1.4142")),

    // sentence first, then the substring; position may be 0-based (8) or 1-based (9).
    // A -1 fails "find found": that is what a program reading the inputs in the wrong order prints.
    new(1, "find", "find found",          ["find", "Dnes je pekny den", "pekny", "exit"], Found("8", "9")),
    new(1, "find", "find not found",      ["find", "Dnes je pekny den", "auto", "exit"],  NotFound()),

    // the assistant keeps running until "exit": a program that quits after one command fails this
    new(1, "menu", "two commands in a row", ["sum", "20", "3", "odm", "81", "exit"],   HasAll("23", "9")),

    // edge cases: the extra valid value lets a program that re-asks for input finish cleanly
    new(1, "edge", "odm -4",              ["odm", "-4", "4", "exit"],       NoCrash("NaN"), Edge: true),
    // text instead of a number must be rejected, not turned into 0: any 0 in the output = silent zero
    // (if a correct program fails these, check whether its error message mentions 0)
    // sum: if "abc" became 0, the program prints 0 + 40 = 40, often as just "40" -> RejectsText
    new(1, "edge", "sum with text",       ["sum", "abc", "40", "abc", "2", "exit"], RejectsText("40", "42"), Edge: true),
    new(1, "edge", "odm with text",       ["odm", "abc", "16", "exit"],             NoCrash("0"), Edge: true),
    // must react with some message, not silently show the menu again
    new(1, "edge", "unknown command",     ["xyz", "exit"],                  AllOf(NoCrash(), Responds()), Edge: true),

    // ---------- PHASE 2: rect mode (area, perimeter, diagonal) ----------
    // "exit" twice: inside a mode, exit may go back to the main menu; the second one ends the program
    new(2, "area",      "area 5x7",       ["rect", "5", "7", "area", "exit", "exit"],      Has("35")),
    new(2, "area",      "area 3x4",       ["rect", "3", "4", "area", "exit", "exit"],      Has("12")),

    new(2, "perimeter", "perimeter 5x7",  ["rect", "5", "7", "perimeter", "exit", "exit"], Has("24")),
    new(2, "perimeter", "perimeter 3x4",  ["rect", "3", "4", "perimeter", "exit", "exit"], Has("14")),

    new(2, "diagonal",  "diagonal 5x7",   ["rect", "5", "7", "diagonal", "exit", "exit"],  Has("8.6023")),
    new(2, "diagonal",  "diagonal 3x4",   ["rect", "3", "4", "diagonal", "exit", "exit"],  Has("5")),

    // the mode keeps answering questions about the same rectangle (like the example in the assignment)
    new(2, "rect",      "rect 3 queries", ["rect", "5", "7", "area", "perimeter", "diagonal", "exit", "exit"],
                                          HasAll("35", "24", "8.6023")),

    new(2, "edge", "rect text second side", ["rect", "5", "abc", "7", "perimeter", "exit", "exit"], NoCrash("10"), Edge: true),

    // invalid sides must be rejected: no crash, and no result computed from them
    new(2, "edge", "rect negative side",  ["rect", "-5", "5", "5", "7", "area", "exit", "exit"],      NoCrash("-25"), Edge: true),
    new(2, "edge", "rect zero side",      ["rect", "0", "9", "5", "7", "perimeter", "exit", "exit"],  NoCrash("18"),  Edge: true),
    // "abc" must not become 0: a 0x5 rectangle would have perimeter 10
    new(2, "edge", "rect text side",      ["rect", "abc", "5", "7", "perimeter", "exit", "exit"],      NoCrash("10"),  Edge: true),

    // ---------- PHASE 3: array mode (find, min, sort) ----------
    // position may be 1-based (like the example in the assignment) or 0-based
    new(3, "array find", "find 70 in 50 10 70 40",     ["array", "50 10 70 40", "find", "70", "exit", "exit"],     HasAny("3", "2")),
    new(3, "array find", "find 70 in 90 40 -20 70 10", ["array", "90 40 -20 70 10", "find", "70", "exit", "exit"], HasAny("4", "3")),

    new(3, "min",  "min 50 10 70 40",      ["array", "50 10 70 40", "min", "exit", "exit"],      Has("10")),
    new(3, "min",  "min 90 40 -20 70 10",  ["array", "90 40 -20 70 10", "min", "exit", "exit"],  Has("-20")),

    new(3, "sort", "sort 50 10 70 40",     ["array", "50 10 70 40", "sort", "exit", "exit"],     Sequence("10 40 50 70")),
    new(3, "sort", "sort 90 40 -20 70 10", ["array", "90 40 -20 70 10", "sort", "exit", "exit"], Sequence("-20 10 40 70 90")),

    // the mode keeps answering questions about the same array (like the example in the assignment)
    new(3, "array", "array 2 queries",     ["array", "50 10 70 40", "find", "70", "sort", "exit", "exit"],
                                           AllOf(HasAny("3", "2"), Sequence("10 40 50 70"))),

    // the assignment says a space follows every number, so a trailing space is valid input
    new(3, "edge", "array trailing space", ["array", "50 10 70 40 ", "min", "exit", "exit"],              Has("10"), Edge: true),
    // text must not become 0: a minimum of 0, or finding 0 (position 1 / index 0), means silent zero
    new(3, "edge", "array with text",      ["array", "50 x 70", "50 10 70 40", "min", "exit", "exit"],   NoCrash("0"), Edge: true),
    new(3, "edge", "find with text",       ["array", "0 50 40 70", "find", "abc", "70", "exit", "exit"], NoCrash("0", "1"), Edge: true),
    new(3, "edge", "find missing value",   ["array", "50 10 70 40", "find", "99", "exit", "exit"],       AllOf(NoCrash(), NotFound()), Edge: true),
};

// ---------- BASELINES: what the program prints without being asked anything ----------
// Before the tests, each program is run with these inputs to capture its menus and prompts
// ("1. sum", "4. help", "Vyber 1-5:"). Checks for answers then ignore that text, so a "4" in the
// menu can't pass "odm 16". A test starting with "rect" also uses the rect baseline. Its numbers
// differ from the tests, so a result the program prints on its own can't remove a real answer.
var baselines = new Dictionary<string, string[]>
{
    [""]      = ["exit"],
    ["rect"]  = ["rect", "2", "9", "exit", "exit"],
};
// Array tests get their own baseline: the test's inputs up to the first question, then exit.
// A program that prints the array back ("Pole: 50 10 70 40") would otherwise "answer" min with it.
string[] arrayQuestions = ["find", "min", "sort"];
// Compared with help: a menu printed anyway doesn't count as help (see HelpLists)
string[] unknownCommandInputs = ["xyz", "exit"];
// =========================================================


// ================= STATIC ANALYSIS: CHECKS =================
// new(column name, command, return type, parameter types, Depth, Phase)
//   types: "void", "int", "double"...   "num" = any number type   "*" = anything
//   Depth 0 = the code handling the command must call the function directly (default)
//   Depth 1 = it may also go through one helper function first
//   Phase: the check is required for that phase's points. For a command that phase also tests
//          (area, perimeter, diagonal, rect) it must pass together with the tests; any other
//          (sum, odm, find, help) counts as one more command of that phase.
//          Without Phase the check is only a column for your judgement.
// Function names don't matter.
var staticChecks = new List<StaticCheck>
{
    new("P2 sum: own void() function",  "sum",  "void", [], Phase: 2),
    new("P2 odm: own void() function",  "odm",  "void", [], Phase: 2),
    new("P2 find: own void() function", "find", "void", [], Phase: 2),
    new("P2 help: own void() function", "help", "void", [], Phase: 2),
    new("P2 rect: own void() function", "rect", "void", [], Phase: 2),
    new("P2 area: num(num,num)",        "area",      "num", ["num", "num"], Depth: 1, Phase: 2),
    new("P2 perimeter: num(num,num)",   "perimeter", "num", ["num", "num"], Depth: 1, Phase: 2),
    new("P2 diagonal: num(num,num)",    "diagonal",  "num", ["num", "num"], Depth: 1, Phase: 2),
};
// ===========================================================


// ================= STEP 1: CLONE / UPDATE REPOS =================
if (File.Exists(reposFile))
{
    Directory.CreateDirectory(submissionsDir);
    foreach (var line in File.ReadAllLines(reposFile))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
        var parts = line.Split(';', 2);
        if (parts.Length != 2) { Console.WriteLine($"Skipping bad line: {line}"); continue; }

        var name   = parts[0].Trim();
        var url    = parts[1].Trim();
        var target = Path.Combine(submissionsDir, name);

        var r = Directory.Exists(Path.Combine(target, ".git"))
            ? Run("git", $"-C \"{target}\" pull", [], buildTimeout)
            : Run("git", $"clone \"{url}\" \"{target}\"", [], buildTimeout);

        Console.WriteLine(r.ExitCode == 0 ? $"Fetched {name}" : $"FAILED to fetch {name}: {r.Output}");
    }
}
else
{
    Console.WriteLine($"No {reposFile} found in {Path.GetFullPath(".")}");
}

if (!Directory.Exists(submissionsDir))
{
    Console.WriteLine($"No submissions folder found at '{submissionsDir}'.");
    return;
}
// ================================================================


// ================= STEP 2: GRADE EACH STUDENT =================
// command names used to recognise the assignment project in a repo
var commandWords = scenarios.Select(s => s.Inputs[0])
    .Concat(staticChecks.Select(c => c.Command ?? ""))
    .Where(w => w != "").Distinct().ToArray();

var reportRows = new List<ReportRow>();
var results = new StringBuilder();
results.AppendLine(string.Join(";",
    new[] { "Student", "Build", "P1", "P2", "P3", "Setup penalty", "Total" }
        .Concat(scenarios.Select(ColumnName))
        .Concat(staticChecks.Select(c => c.Name))));

// Saved after every student, so a crash or Ctrl+C later on keeps the results so far.
// A file open in Excel can't be overwritten: that is reported, and the next save tries again.
bool SaveResults()
{
    try
    {
        File.WriteAllText(resultsFile, results.ToString(), Encoding.UTF8); // UTF-8 with BOM so Excel shows diacritics
        File.WriteAllText(reportFile, BuildReport(scenarios, staticChecks, reportRows), Encoding.UTF8);
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   Could not save results (is {resultsFile} open in Excel?): {ex.Message}");
        return false;
    }
}

foreach (var repo in Directory.GetDirectories(submissionsDir).OrderBy(d => d))
{
    var student = Path.GetFileName(repo);
    Console.WriteLine($"== {student}");

    // One broken submission must not stop the others: any error is recorded as GRADER ERROR for this student.
    try
    {
        // ---------- 2a. Pick the project ----------
        // Repos often hold class exercises next to the assignment: take the project whose code
        // mentions the most command names ("sum", "rect", ...).
        var csproj     = PickProject(repo, commandWords);
        var projectDir = csproj is null ? repo : Path.GetDirectoryName(csproj)!;

        // ---------- 2b. Build ----------
        var buildStatus = "OK";
        var buildErrors = "";
        var codeDir     = projectDir; // folder with the code that actually gets graded
        var penalty     = 0;
        var outDir      = Path.Combine(Path.GetTempPath(), "AssistantGrader", student);

        var build = csproj is null ? null : BuildProject(csproj, outDir, buildTimeout);
        string? dll = build?.Dll;

        if (dll is null)
        {
            if (build != null) SaveLog(logsDir, student, "build", build.Output);
            buildErrors = build is null
                ? "No .csproj project file found."
                : string.Join("\n", build.Output.Split('\n')
                    .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Select(l => l.Trim()).Distinct().Take(10));

            // Fallback: build just the assistant's .cs files with a standard project. If that works,
            // the code is fine and only the setup was wrong -> grade it, minus setupPenalty.
            var rebuildDir = AssistantCodeFolder(projectDir, commandWords);
            var rebuild    = rebuildDir is null ? null : BuildRebuilt(rebuildDir, student, outDir + "-rebuilt", buildTimeout);

            if (rebuild?.Dll != null)
            {
                dll         = rebuild.Dll;
                codeDir     = rebuildDir!;
                penalty     = setupPenalty;
                buildStatus = build is null ? "REBUILT: no project file" : "REBUILT: project didn't build";
            }
            else
            {
                buildStatus = build is null ? "NO PROJECT" : "BUILD FAILED";
                if (rebuild != null) SaveLog(logsDir, student, "build-rebuilt", rebuild.Output);
            }
        }

        // ---------- 2c. Static analysis (also when the build failed) ----------
        var roots         = ParseStudentCode(codeDir);
        var functions     = CollectFunctions(roots);
        var staticResults = staticChecks.Select(c => RunStaticCheck(roots, functions, c)).ToList();

        // ---------- 2d. Functional scenarios ----------
        // What the program prints anyway (see BASELINES). Each input list runs once per student.
        var baselineCache = new Dictionary<string, string>();
        string Baseline(string[] inputs, string logName)
        {
            var key = string.Join("\n", inputs);
            if (!baselineCache.TryGetValue(key, out var output))
            {
                output = Run("dotnet", $"\"{dll}\"", inputs, scenarioTimeout).Output;
                SaveLog(logsDir, student, $"baseline {logName}", output);
                baselineCache[key] = output;
            }
            return output;
        }

        var scenarioResults = new List<ScenarioResult>();
        foreach (var s in scenarios)
        {
            if (dll is null) { scenarioResults.Add(new(s, "NOT RUN", "", "")); continue; }

            var r = Run("dotnet", $"\"{dll}\"", s.Inputs, scenarioTimeout);
            SaveLog(logsDir, student, ColumnName(s), r.Output); // what the program printed, for manual review

            var baseline = Baseline(baselines[""], "main");
            if (baselines.TryGetValue(s.Inputs[0], out var modeInputs))
                baseline += "\n" + Baseline(modeInputs, s.Inputs[0]);
            if (s.Inputs[0] == "array")
            {
                string[] arrayInputs = [.. s.Inputs.TakeWhile(i => !arrayQuestions.Contains(i)), "exit", "exit"];
                baseline += "\n" + Baseline(arrayInputs, "array " + string.Join(" ", arrayInputs[1..^2]));
            }
            r = r with
            {
                AnswerOutput         = WithoutBaseline(r.Output, baseline),
                UnknownCommandOutput = Baseline(unknownCommandInputs, "unknown command"),
            };

            bool ok = s.Check.Test(r);
            var result = ok                    ? (r.TimedOut ? "PASS (no exit)" : "PASS")   // right answer, but "exit" didn't end it
                       : IsConsoleIssue(r.Output) ? "CHECK: console call"                   // see IsConsoleIssue below
                       : r.TimedOut            ? "TIMEOUT"
                       : !r.Output.Any(char.IsLetterOrDigit) ? "FAIL (no output)"          // the program printed nothing
                       :                         "FAIL";
            scenarioResults.Add(new(s, result, r.Output, r.AnswerOutput));
        }

        // ---------- 2e. Rubric points ----------
        var codeResults = staticChecks.Zip(staticResults).ToList();
        var points = new[] { 1, 2, 3 }.Select(phase => PhasePoints(scenarioResults, codeResults, phase, dll != null)).ToList();
        var total  = Math.Max(0, points.Sum() - penalty);

        results.AppendLine(string.Join(";",
            new[] { student, buildStatus }
                .Concat(points.Select(p => p.ToString()))
                .Append(penalty == 0 ? "" : $"-{penalty}")
                .Append(total.ToString())
                .Concat(scenarioResults.Select(x => x.Result))
                .Concat(staticResults)));

        reportRows.Add(new ReportRow(student, buildStatus, buildErrors, points, penalty, total, scenarioResults, staticResults));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   GRADER ERROR for {student}: {ex.Message}");
        try { SaveLog(logsDir, student, "grader-error", ex.ToString()); } catch { }

        var zero = new List<int> { 0, 0, 0 };
        results.AppendLine(string.Join(";",
            new[] { student, "GRADER ERROR", "0", "0", "0", "", "0" }
                .Concat(scenarios.Select(_ => "NOT RUN"))
                .Concat(staticChecks.Select(_ => ""))));
        reportRows.Add(new ReportRow(student, "GRADER ERROR", ex.Message, zero, 0, 0,
            scenarios.Select(s => new ScenarioResult(s, "NOT RUN", "", "")).ToList(), staticChecks.Select(_ => "").ToList()));
    }

    SaveResults();
}
// ==============================================================


// ================= STEP 3: SAVE RESULTS =================
Console.WriteLine(SaveResults()
    ? $"Done -> {resultsFile}, {reportFile}"
    : $"Done, but the results could NOT be saved. Close {resultsFile} / {reportFile} and run again.");
// ========================================================




// #####################################################################
//                               HELPERS
// #####################################################################


// ================= HELPERS: RUBRIC POINTS =================
// Per phase: all commands pass + all edge cases pass = 40, all commands pass = 35,
// at least one command passes = 20, otherwise (or no build) = 0.
// A command passes when ALL its non-edge scenarios pass, and its code check too when that check
// is required for this phase (e.g. P2 area must work AND call a num(num,num) function).
// A required code check for a command not tested in this phase (P2 sum) counts as its own command.
static int PhasePoints(List<ScenarioResult> results, List<(StaticCheck Check, string Result)> code, int phase, bool built)
{
    if (!built) return 0;

    var inPhase = results.Where(x => x.Scenario.Phase == phase).ToList();
    var works   = inPhase.Where(x => !x.Scenario.Edge).GroupBy(x => x.Scenario.Command)
                         .ToDictionary(g => g.Key, g => g.All(x => x.Result.StartsWith("PASS")));
    foreach (var (check, result) in code.Where(c => c.Check.Phase == phase))
    {
        var key = check.Command ?? check.Name;
        works[key] = works.GetValueOrDefault(key, true) && result == "PASS";
    }
    if (works.Count == 0) return 0;

    int working  = works.Values.Count(w => w);
    bool edgesOk = inPhase.Where(x => x.Scenario.Edge).All(x => x.Result.StartsWith("PASS"));

    if (working == works.Count) return edgesOk ? 40 : 35;
    return working > 0 ? 20 : 0;
}

static string ColumnName(Scenario s) => $"P{s.Phase} {s.Name}{(s.Edge ? " [edge]" : "")}";
// ==========================================================


// ================= HELPERS: HTML REPORT =================
// One row per student, one column per test. A cell shows the result; click it to see exactly
// what the grader typed in and what the student's program printed.
static string BuildReport(List<Scenario> scenarios, List<StaticCheck> staticChecks, List<ReportRow> rows)
{
    string E(string text) => System.Net.WebUtility.HtmlEncode(text);
    string Css(string result) =>
        result.StartsWith("PASS") ? "pass"
        // NO BRANCH costs points but may be the grader's mistake (e.g. commands in a Dictionary): look at it
        : result.StartsWith("CHECK") || result is "TIMEOUT" or "NO BRANCH" ? "warn"
        : result is "NOT RUN" or "" ? "grey"
        : result == "OK" ? "pass" : "fail";
    // trailing spaces are invisible, so show them as ␣
    string Typed(string line) => E(line.TrimEnd()) + new string('␣', line.Length - line.TrimEnd().Length);
    string Cut(string text) => text.Length <= 4000 ? text : text[..4000] + "\n… (cut, full output in the logs folder)";

    var h = new StringBuilder();
    h.Append("""
        <!DOCTYPE html>
        <html lang="sk"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>AssistantGrader – report</title>
        <style>
        :root { --bg:#fff; --fg:#1d1d1f; --muted:#6e6e73; --line:#d2d2d7; --head:#f5f5f7; --code:#fafafa;
                --pass:#dcf5e3; --pass-fg:#13652b; --fail:#fde2df; --fail-fg:#a3221a;
                --warn:#fff2cc; --warn-fg:#7a5000; --grey:#ededf0; --grey-fg:#555; color-scheme:light; }
        @media (prefers-color-scheme: dark) {
          :root { --bg:#1c1c1e; --fg:#f2f2f7; --muted:#a1a1a6; --line:#3a3a3c; --head:#2c2c2e; --code:#111;
                  --pass:#173d22; --pass-fg:#86e3a0; --fail:#4a1c19; --fail-fg:#ff9b92;
                  --warn:#463712; --warn-fg:#ffd36b; --grey:#2c2c2e; --grey-fg:#bbb; color-scheme:dark; } }
        body { margin:0; padding:16px; font:13px/1.4 system-ui, -apple-system, "Segoe UI", sans-serif; background:var(--bg); color:var(--fg); }
        h1 { font-size:18px; margin:0 0 4px; }
        .bar { display:flex; gap:8px; align-items:center; margin:8px 0 12px; flex-wrap:wrap; color:var(--muted); }
        button { font:inherit; padding:4px 10px; border:1px solid var(--line); border-radius:6px; background:var(--head); color:var(--fg); cursor:pointer; }
        .wrap { overflow:auto; max-height:calc(100vh - 110px); border:1px solid var(--line); border-radius:6px; }
        table { border-collapse:separate; border-spacing:0; }
        th, td { border-right:1px solid var(--line); border-bottom:1px solid var(--line); padding:6px 8px; vertical-align:top; text-align:left; }
        thead th { position:sticky; top:0; z-index:2; background:var(--head); min-width:150px; max-width:220px; font-weight:600; }
        th.name { position:sticky; left:0; z-index:1; background:var(--head); min-width:170px; }
        thead th.name { z-index:3; }
        th.edge { box-shadow:inset 0 3px 0 #e0a800; }
        th.code { box-shadow:inset 0 3px 0 #7d7dff; }
        .x { color:var(--muted); font-weight:400; font-size:11px; margin-top:3px; }
        .pts { display:block; font-weight:400; color:var(--muted); font-size:12px; margin-top:2px; }
        td { min-width:150px; }
        td.pass { background:var(--pass); color:var(--pass-fg); }
        td.fail { background:var(--fail); color:var(--fail-fg); }
        td.warn { background:var(--warn); color:var(--warn-fg); }
        td.grey { background:var(--grey); color:var(--grey-fg); }
        summary { cursor:pointer; font-weight:600; }
        .lbl { font-size:11px; color:var(--muted); margin-top:6px; }
        pre { white-space:pre-wrap; word-break:break-word; max-width:380px; max-height:320px; overflow:auto; margin:3px 0 6px;
              padding:6px; background:var(--code); color:var(--fg); border:1px solid var(--line); border-radius:4px;
              font:12px/1.35 ui-monospace, Consolas, monospace; }
        </style></head><body>
        <h1>AssistantGrader – report</h1>
        <div class="bar">
          <button onclick="document.querySelectorAll('details').forEach(d => d.open = true)">Expand all</button>
          <button onclick="document.querySelectorAll('details').forEach(d => d.open = false)">Collapse all</button>
          <span>Click a result to see what was typed and what the program printed.
          Yellow top border = edge case, blue = code check. ␣ = a trailing space.</span>
        </div>
        <div class="wrap"><table><thead><tr><th class="name">Student</th><th>Build</th>
        """);

    foreach (var s in scenarios)
        h.Append($"<th class=\"{(s.Edge ? "edge" : "")}\">{E(ColumnName(s))}" +
                 $"<div class=\"x\">typed: {string.Join(" · ", s.Inputs.Select(Typed))}</div>" +
                 $"<div class=\"x\">expects: {E(s.Check.Description)}</div></th>");
    foreach (var c in staticChecks)
        h.Append($"<th class=\"code\">{E(c.Name)}<div class=\"x\">" +
                 (c.Phase is int ph ? $"code check, required for P{ph} points" : "code check (no points)") + "</div></th>");
    h.Append("</tr></thead><tbody>");

    foreach (var row in rows)
    {
        h.Append($"<tr><th class=\"name\">{E(row.Student)}<span class=\"pts\">" +
                 $"P1 {row.Points[0]} · P2 {row.Points[1]} · P3 {row.Points[2]}" +
                 (row.Penalty > 0 ? $" − {row.Penalty} setup" : "") + $" = {row.Total}</span></th>");

        var buildCss = row.Build == "OK" ? "pass" : row.Build.StartsWith("REBUILT") ? "warn" : "fail";
        h.Append(row.BuildErrors == ""
            ? $"<td class=\"{buildCss}\">{E(row.Build)}</td>"
            : $"<td class=\"{buildCss}\"><details><summary>{E(row.Build)}</summary><pre>{E(row.BuildErrors)}</pre></details></td>");

        foreach (var (s, result, output, answers) in row.Runs)
        {
            if (result == "NOT RUN") { h.Append("<td class=\"grey\">NOT RUN</td>"); continue; }
            h.Append($"<td class=\"{Css(result)}\"><details><summary>{E(result)}</summary>" +
                     $"<div class=\"lbl\">Typed</div><pre>{string.Join("\n", s.Inputs.Select(Typed))}</pre>" +
                     $"<div class=\"lbl\">Printed</div><pre>{E(Cut(output))}</pre>" +
                     $"<div class=\"lbl\">Answers checked (menus and prompts removed)</div><pre>{E(Cut(answers.Trim()))}</pre></details></td>");
        }
        foreach (var result in row.StaticResults)
            h.Append($"<td class=\"{Css(result)}\">{E(result)}</td>");
        h.Append("</tr>");
    }
    h.Append($"</tbody></table></div><p class=\"x\">Generated {DateTime.Now:yyyy-MM-dd HH:mm}</p></body></html>");
    return h.ToString();
}
// ========================================================


// ================= HELPERS: CHECKS USED IN SCENARIOS =================
// Each check has a short description (shown in report.html) and the test itself.
static Check Has(string token) =>
    new($"contains {token}", r => OutputHas(r,token));

static Check HasAny(params string[] tokens) =>
    new($"contains one of: {string.Join(", ", tokens)}", r => tokens.Any(t => OutputHas(r,t)));

static Check HasAll(params string[] tokens) =>
    new($"contains all of: {string.Join(", ", tokens)}", r => tokens.All(t => OutputHas(r,t)));

// Text typed instead of a number must not quietly become 0. Fails on a crash, on any 0 in the output,
// or when zeroResult shows up without correctResult (0 + 40 = 40 printed as just "40").
static Check RejectsText(string zeroResult, string correctResult) =>
    new($"no crash, no 0, and not {zeroResult} without {correctResult}",
        r => NoCrash("0").Test(r) && !(OutputHas(r,zeroResult) && !OutputHas(r,correctResult)));

static Check AllOf(params Check[] checks) =>
    new(string.Join("  AND  ", checks.Select(c => c.Description)), r => checks.All(c => c.Test(r)));

// A program that prints nothing at all doesn't count as handling anything.
static Check NoCrash(params string[] forbidden) =>
    new(forbidden.Length == 0 ? "no crash" : $"no crash, and no {string.Join(", ", forbidden)} in the output",
        r => r.ExitCode == 0 && !r.TimedOut
             && r.Output.Any(char.IsLetterOrDigit)
             && !r.Output.Contains("Unhandled exception")
             && !forbidden.Any(t => OutputHas(r,t)));

// Prints something besides its menus and prompts (e.g. "Neznámy príkaz")
static Check Responds() =>
    new("prints a message, not just the menu again", r => r.AnswerOutput.Any(char.IsLetterOrDigit));

// Each word must appear more often than after an unknown command: a menu that is printed
// anyway (at the start, after every command) then doesn't count as help.
static Check HelpLists(params string[] words) =>
    new($"lists {string.Join(", ", words)} (more often than after an unknown command)",
        r => words.All(w => CountWord(r.Output, w) > CountWord(r.UnknownCommandOutput, w)));

static Check Sequence(string numbers)
{
    // BigInteger, not int: a student may print a number too long for int, which must not crash the grader
    var expected = numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(n => System.Numerics.BigInteger.Parse(n)).ToArray();
    return new($"numbers in this order: {numbers}", r =>
    {
        var found = Regex.Matches(r.AnswerOutput, @"(?<!\d)-?\d+").Select(m => System.Numerics.BigInteger.Parse(m.Value)).ToArray();
        for (int i = 0; i + expected.Length <= found.Length; i++)
            if (found.Skip(i).Take(expected.Length).SequenceEqual(expected)) return true;
        return false;
    });
}

// Compared without diacritics. Stems match the start of a word ("nachadz" -> "nachádza"),
// whole words only themselves ("nie" doesn't match "niečo", "not" doesn't match "nothing").
// Labels like "pozícia" / "index" are not answers: "Pozícia: -1" means not found.
static bool SaysFound(string text) =>
    new[] { "nachadz", "nasiel", "nasla", "naslo", "najden", "obsahuje", "je vo vete" }.Any(w => HasWord(text, w))
    || new[] { "ano", "yes", "true", "found" }.Any(w => HasWord(text, w, wholeWord: true));

static bool SaysNotFound(string text) =>
    new[] { "nenachadz", "nenasiel", "nenasla", "nenaslo", "nenajden", "neobsahuje" }.Any(w => HasWord(text, w))
    || new[] { "nie", "neni", "not", "false" }.Any(w => HasWord(text, w, wholeWord: true));

// A -1 always fails: a program that reads the inputs in the wrong order prints "Pozícia: -1"
static Check Found(params string[] positions) =>
    new($"position {string.Join(" or ", positions)}, or a 'found' answer (nachádza, áno, true...), and no -1",
        r => !HasNumber(r.AnswerOutput, "-1")
             && (positions.Any(p => HasNumber(r.AnswerOutput, p))
                 || (SaysFound(r.AnswerOutput) && !SaysNotFound(r.AnswerOutput))));

static Check NotFound() =>
    new("-1, or a 'not found' answer (nenachádza, nie, false...)",
        r => HasNumber(r.AnswerOutput, "-1") || SaysNotFound(r.AnswerOutput));
// =====================================================================


// ================= HELPERS: OUTPUT MATCHING =================
// Numbers are looked for in AnswerOutput (menus and prompts removed, see WithoutBaseline),
// words in the full output (so "help" still finds command names in a menu).
static bool OutputHas(RunResult r, string token) =>
    Regex.IsMatch(token, @"^-?\d+(\.\d+)?$") ? HasNumber(r.AnswerOutput, token) : HasWord(r.Output, token);

// ignore crash reports: their stack traces contain line numbers like "line 10"
static string WithoutCrashReport(string text) =>
    Regex.Replace(text, @"^(Unhandled exception.*|\s+at .*|\s*---.*)$", "", RegexOptions.Multiline);

static bool HasNumber(string text, string number) =>
    Regex.Matches(WithoutCrashReport(text), @"(?<![\d.,])-?\d+(?:[.,]\d+)?").Any(m => SameNumber(m.Value, number));

static bool HasWord(string text, string word, bool wholeWord = false) =>
    WordRegex(word, wholeWord).IsMatch(WithoutCrashReport(text).ToLowerInvariant());

static int CountWord(string text, string word) =>
    WordRegex(word, false).Matches(WithoutCrashReport(text).ToLowerInvariant()).Count;

static Regex WordRegex(string token, bool wholeWord)
{
    // Each letter of the word may also be 1-2 non-ASCII characters: that covers real Slovak letters
    // (á, š...) and the garbage Windows makes of them when the console encodings don't match.
    var word = string.Concat(Simplify(token).Select(ch => char.IsLetter(ch)
        ? $@"(?:{ch}|[^\x00-\x7F]{{1,2}})"
        : Regex.Escape(ch.ToString())));
    // a whole word must not continue with a letter (or a non-ASCII character, which may be a garbled one)
    var end = wholeWord ? @"(?![\p{L}\d]|[^\x00-\x7F])" : "";
    return new Regex($@"(?<![\p{{L}}\d]){word}{end}");
}

// Removes the program's menus and prompts from a test's output, using its baseline output:
//  - lines that also appear in the baseline ("1. sum", "4. help") are dropped
//  - prompts printed with Console.Write end up glued to the next line ("Vyber 1-5: Vysledok: 4"),
//    so a line starting with baseline text up to a ':', '?' or '>' loses that start ("Vysledok: 4")
static string WithoutBaseline(string output, string baseline)
{
    var baseLines = baseline.Split('\n').Select(l => l.Trim()).Where(l => l != "").ToHashSet();
    var prompts = baseLines
        .SelectMany(l => l.Select((ch, i) => (ch, i)).Where(x => x.ch is ':' or '?' or '>').Select(x => l[..(x.i + 1)]))
        .Distinct().OrderByDescending(p => p.Length).ToList();

    string StripPrompts(string line)
    {
        line = line.Trim();
        string? Prompt() => prompts.FirstOrDefault(p => line.StartsWith(p, StringComparison.Ordinal));
        for (var p = Prompt(); p != null; p = Prompt())
            line = line[p.Length..].TrimStart();
        return line;
    }

    // The same text may be glued to a prompt in the baseline but not in the test, or the other way
    // round ("...medzerou: Pole: 0 50 40 70" vs "Pole: 0 50 40 70"), so the baseline also knows every
    // line's ending after each ':', '?' or '>', and test lines are compared with and without prompts.
    var endings = baseLines
        .SelectMany(l => l.Select((ch, i) => (ch, i)).Where(x => x.ch is ':' or '?' or '>').Select(x => l[(x.i + 1)..].Trim()))
        .Where(l => l != "").ToList();
    baseLines.UnionWith(endings);
    baseLines.UnionWith(baseLines.Select(StripPrompts).Where(l => l != "").ToList());

    return string.Join("\n", output.Split('\n')
        .Where(l => !baseLines.Contains(l.Trim()))
        .Select(StripPrompts)
        .Where(l => !baseLines.Contains(l)));
}

static bool SameNumber(string printed, string expected)
{
    var a = double.Parse(printed.Replace(',', '.'), CultureInfo.InvariantCulture);
    var e = double.Parse(expected, CultureInfo.InvariantCulture);
    int da = Decimals(printed), de = Decimals(expected);

    if (de == 0) return Math.Abs(a - e) < 1e-9;   // whole number expected: must be exact (35 or 35.0)
    if (da == 0) return false;                     // decimal expected, whole number printed (9 for 8.6)

    int p = Math.Min(da, de);                      // compare at the precision the student printed
    return Math.Abs(Math.Round(a, p, MidpointRounding.AwayFromZero)
                  - Math.Round(e, p, MidpointRounding.AwayFromZero)) < 1e-9;
}

static int Decimals(string n)
{
    var i = n.IndexOfAny(['.', ',']);
    return i < 0 ? 0 : n.Length - i - 1;
}

// lower case, no diacritics:  "Nachádza" -> "nachadza"
static string Simplify(string s) =>
    new string(s.Normalize(NormalizationForm.FormD)
        .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
        .ToArray()).ToLowerInvariant();

// Console.ReadKey() and (on Windows) Console.Clear() crash when the grader types the input
// for the student. That's the grader's fault, not theirs, so those get flagged for a manual look.
static bool IsConsoleIssue(string output) =>
    output.Contains("Cannot read keys") || output.Contains("ReadKey") || output.Contains("Console.Clear")
    || output.Contains("ConsolePal.Clear");
// ============================================================


// ================= HELPERS: RUNNING PROCESSES =================
// Starts a process, types the inputs line by line, captures all output, kills it on timeout.
static RunResult Run(string file, string arguments, string[] inputs, TimeSpan timeout)
{
    var psi = new ProcessStartInfo(file, arguments)
    {
        RedirectStandardInput  = true,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
        CreateNoWindow         = true,
    };

    // Students target whatever .NET their computer had (net8.0, net9.0...): run them on the newest
    // installed .NET instead of failing with "You must install or update .NET".
    psi.Environment["DOTNET_ROLL_FORWARD"] = "LatestMajor";

    using var p = new Process { StartInfo = psi };
    var output = new StringBuilder();
    // A program stuck in a loop can print hundreds of MB in a few seconds: stop it after 1 MB.
    const int maxOutput = 1_000_000;
    bool capped = false;
    void Collect(string? line)
    {
        if (line is null) return;
        lock (output)
        {
            if (capped) return;
            output.AppendLine(line);
            if (output.Length <= maxOutput) return;
            capped = true;
            output.AppendLine("… (stopped: the program printed more than 1 MB, probably an endless loop)");
        }
        try { p.Kill(entireProcessTree: true); } catch { }
    }
    p.OutputDataReceived += (_, e) => Collect(e.Data);
    p.ErrorDataReceived  += (_, e) => Collect(e.Data);

    p.Start();
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();

    try
    {
        foreach (var line in inputs) p.StandardInput.WriteLine(line);
        p.StandardInput.Close();
    }
    catch (IOException) { /* program exited before reading all input */ }

    bool finished = p.WaitForExit((int)timeout.TotalMilliseconds);
    if (!finished)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
    }
    p.WaitForExit(); // let the remaining output flush

    // stopped for too much output counts like a timeout: it never ended on its own
    lock (output) return new RunResult(finished && !capped ? p.ExitCode : -1, output.ToString(), !finished || capped);
}

static void SaveLog(string logsDir, string student, string name, string content)
{
    var invalid  = Path.GetInvalidFileNameChars();
    var safeName = string.Concat(name.Select(ch => invalid.Contains(ch) ? '_' : ch));
    var dir      = Path.Combine(logsDir, student);
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, safeName + ".log"), content);
}
// ==============================================================


// ================= HELPERS: STATIC ANALYSIS =================
// Every student .cs file in a folder and its subfolders, skipping bin/obj build output.
static IEnumerable<string> StudentCsFiles(string dir)
{
    var sep = Path.DirectorySeparatorChar;
    return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}"));
}

// dotnet build into outDir. Dll is the program to run, or null when the build failed.
static BuildResult BuildProject(string csproj, string outDir, TimeSpan timeout)
{
    try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { } // no leftovers from earlier runs
    var build = Run("dotnet", $"build \"{csproj}\" -c Release -o \"{outDir}\"", [], timeout);
    var runtimeConfig = Directory.Exists(outDir) ? Directory.GetFiles(outDir, "*.runtimeconfig.json").FirstOrDefault() : null;
    return new(build.Output, build.ExitCode == 0 && runtimeConfig != null
        ? runtimeConfig.Replace(".runtimeconfig.json", ".dll") : null);
}

// The folder with the assistant's code: where the .cs file mentioning the most command names is.
// Falls back to the folder itself if it has .cs files directly in it.
static string? AssistantCodeFolder(string dir, string[] commandWords)
{
    var best = StudentCsFiles(dir)
        .Select(f => (File: f, Score: commandWords.Count(w => File.ReadAllText(f).Contains($"\"{w}\"", StringComparison.OrdinalIgnoreCase))))
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .FirstOrDefault();
    if (best.File != null) return Path.GetDirectoryName(best.File);
    return Directory.GetFiles(dir, "*.cs").Length > 0 ? dir : null;
}

// Copies the .cs files of one folder (not its subfolders) into a fresh standard console project
// for this .NET version, and builds that.
static BuildResult BuildRebuilt(string codeDir, string student, string outDir, TimeSpan timeout)
{
    var dir = Path.Combine(Path.GetTempPath(), "AssistantGrader-rebuilt", student);
    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    Directory.CreateDirectory(dir);
    foreach (var f in Directory.GetFiles(codeDir, "*.cs"))
        File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);

    File.WriteAllText(Path.Combine(dir, "Rebuilt.csproj"), $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net{Environment.Version.Major}.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """);
    return BuildProject(Path.Combine(dir, "Rebuilt.csproj"), outDir, timeout);
}

// The project whose code mentions the most command names as "text" (then: non-test, then by name).
static string? PickProject(string repo, string[] commandWords)
{
    int Score(string csproj)
    {
        var code = string.Concat(StudentCsFiles(Path.GetDirectoryName(csproj)!).Select(File.ReadAllText));
        return commandWords.Count(w => code.Contains($"\"{w}\"", StringComparison.OrdinalIgnoreCase));
    }
    return Directory.GetFiles(repo, "*.csproj", SearchOption.AllDirectories)
        .OrderByDescending(Score)
        .ThenBy(p => p.Contains("Test", StringComparison.OrdinalIgnoreCase))
        .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
}

// Reads the student's .cs files into Roslyn syntax trees.
static List<SyntaxNode> ParseStudentCode(string dir) =>
    StudentCsFiles(dir).Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f)).GetRoot()).ToList();

// Every function the student wrote: class methods AND local functions (top-level statements).
static List<FunctionInfo> CollectFunctions(List<SyntaxNode> roots) =>
    roots.SelectMany(r => r.DescendantNodes())
        .Select(n => n switch
        {
            MethodDeclarationSyntax m      => new FunctionInfo(m.Identifier.Text, m.ReturnType, m.ParameterList, m),
            LocalFunctionStatementSyntax l => new FunctionInfo(l.Identifier.Text, l.ReturnType, l.ParameterList, l),
            _                              => (FunctionInfo?)null
        })
        .OfType<FunctionInfo>()
        .ToList();

static string RunStaticCheck(List<SyntaxNode> roots, List<FunctionInfo> functions, StaticCheck check)
{
    // No command: the function just has to exist somewhere
    if (check.Command is null)
        return functions.Any(f => SignatureMatches(f, check)) ? "PASS" : "FAIL";

    // Command given: find the code that handles it, then check what it calls
    var branches = FindCommandBranches(roots, check.Command);
    if (branches.Count == 0) return "NO BRANCH"; // couldn't find where the command is handled

    return branches.Any(b => CallsMatchingFunction(b, functions, check, 0, new HashSet<string>()))
        ? "PASS" : "FAIL";
}

// Finds the code that runs for a command, e.g.  case "sum": ...  /  if (input == "sum") {...}
// /  "sum" => ...  /  input == "sum" ? ... : ...   (case-insensitive)
static List<SyntaxNode> FindCommandBranches(List<SyntaxNode> roots, string command)
{
    var branches = new List<SyntaxNode>();
    var literals = roots
        .SelectMany(r => r.DescendantNodes().OfType<LiteralExpressionSyntax>())
        .Where(l => l.Token.ValueText.Trim().Equals(command, StringComparison.OrdinalIgnoreCase));

    foreach (var lit in literals)
    {
        foreach (var anc in lit.Ancestors())
        {
            SyntaxNode? branch = anc switch
            {
                SwitchSectionSyntax s         when s.Labels.Any(l => l.Span.Contains(lit.Span)) => s,
                SwitchExpressionArmSyntax a   when a.Pattern.Span.Contains(lit.Span)            => a.Expression,
                IfStatementSyntax i           when i.Condition.Span.Contains(lit.Span)          => i.Statement,
                ConditionalExpressionSyntax c when c.Condition.Span.Contains(lit.Span)          => c.WhenTrue,
                _ => null
            };
            if (branch != null) { branches.Add(branch); break; }
        }
    }
    return branches;
}

// Looks at every call inside the code. If it calls the student's own function with the right
// signature -> true. With Depth 1+ it also follows into the called functions.
static bool CallsMatchingFunction(SyntaxNode node, List<FunctionInfo> functions,
                                  StaticCheck check, int depth, HashSet<string> visited)
{
    if (depth > check.Depth) return false;

    foreach (var call in node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
    {
        var name = CalledName(call);
        if (name is null || !visited.Add(name)) continue;

        foreach (var f in functions.Where(f => f.Name == name))
        {
            if (SignatureMatches(f, check)) return true;
            if (CallsMatchingFunction(f.Node, functions, check, depth + 1, visited)) return true;
        }
    }
    return false;
}

static string? CalledName(InvocationExpressionSyntax call) => call.Expression switch
{
    IdentifierNameSyntax id         => id.Identifier.Text,
    GenericNameSyntax g             => g.Identifier.Text,
    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
    _ => null
};

static bool SignatureMatches(FunctionInfo f, StaticCheck c) =>
    TypeMatches(f.ReturnType.ToString(), c.ReturnType)
    && f.Parameters.Parameters.Count == c.ParamTypes.Length
    && f.Parameters.Parameters.Select((p, i) => TypeMatches(p.Type?.ToString() ?? "", c.ParamTypes[i])).All(x => x);

static bool TypeMatches(string actual, string expected)
{
    var a = NormalizeType(actual);
    return expected switch
    {
        "*"   => true,
        "num" => a is "int" or "long" or "short" or "byte" or "float" or "double" or "decimal",
        _     => a == NormalizeType(expected)
    };
}

// So "Int32" / "System.Int32" count the same as "int", etc.
static string NormalizeType(string t) => t.Replace(" ", "").Replace("System.", "") switch
{
    "Int32"   => "int",
    "Int64"   => "long",
    "Int16"   => "short",
    "Byte"    => "byte",
    "Single"  => "float",
    "Double"  => "double",
    "Decimal" => "decimal",
    "String"  => "string",
    "Boolean" => "bool",
    var x     => x
};
// ============================================================


// ================= TYPES =================
record Scenario(int Phase, string Command, string Name, string[] Inputs, Check Check, bool Edge = false);
record Check(string Description, Func<RunResult, bool> Test);
record ReportRow(string Student, string Build, string BuildErrors, List<int> Points, int Penalty, int Total,
                 List<ScenarioResult> Runs, List<string> StaticResults);
record StaticCheck(string Name, string? Command, string ReturnType, string[] ParamTypes, int Depth = 0, int? Phase = null);
record FunctionInfo(string Name, TypeSyntax ReturnType, ParameterListSyntax Parameters, SyntaxNode Node);
record RunResult(int ExitCode, string Output, bool TimedOut)
{
    // Output without the program's menus and prompts: checks for answers look only here
    public string AnswerOutput { get; init; } = Output;
    // What the program printed for an unknown command (compared with help)
    public string UnknownCommandOutput { get; init; } = "";
}
record ScenarioResult(Scenario Scenario, string Result, string Output, string AnswerOutput);
record BuildResult(string Output, string? Dll);
// =========================================

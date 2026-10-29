# AssistantGrader

Automatic grader for the **"Asistent"** assignment in Programming 1 (C# / .NET). It grades every student the same way: it builds each submission, runs it through a fixed set of test scenarios as if a user were typing, checks the code structure, and scores it by the assignment's rubric (3 phases × 40 points).

For every student it produces:

- **`results.csv`**: one row per student with points and every test result (opens directly in Excel).
- **`report.html`**: the same results in a browser. Click any cell to see exactly what was typed and what the program printed.
- **`logs/<Student>/`**: the full output of every test run, plus build errors.

> **Student data never belongs in this repo.** `submissions/`, `logs/`, `results.csv`, `report.html` and `repos.txt` are in `.gitignore`. Keep it that way.

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (check with `dotnet --version`)
- [Git](https://git-scm.com/) (check with `git --version`)
- Any editor; VS Code with the C# Dev Kit extension works well

## Setup

```
git clone https://github.com/ivanjanovicdigi/AsistantGrader.git
cd AsistantGrader
dotnet build
```

The Roslyn package (`Microsoft.CodeAnalysis.CSharp`) used for the code checks is restored automatically.

`AssistantGrader.csproj` already contains this line, which must stay there. It stops the grader from compiling the students' code as part of itself:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);submissions/**;logs/**</DefaultItemExcludes>
```

---

## Usage

### 1. Get the submissions

**Option A: let the grader download them.** Create `repos.txt` next to `Program.cs`, one student per line, name and repo URL separated by a semicolon:

```
JanNovak;https://gitlab.com/jan/asistent.git
EvaKovacova;https://github.com/eva/asistent.git
# lines starting with # are ignored
```

Use names **without spaces**; they become folder names and column labels. On the first run each repo is cloned into `submissions/<Name>`, on later runs it is updated with `git pull`.

**Option B: put the folders there yourself.** Copy each student's project into `submissions/<Name>`. It doesn't need to be a git repo. Then rename or delete `repos.txt`, and the grader does no git at all: it grades the folders exactly as they are.

**Grading a specific version** (e.g. the last commit before the deadline): rename `repos.txt` so nothing gets pulled, then in that student's folder:

```
git log -1 --before="2026-10-20 23:59" --format="%h %ci"
git checkout <hash>
```

If old build output from other versions breaks the build afterwards, run `git clean -xdf` in that folder.

### 2. Run it

```
dotnet run
```

or, with the submissions somewhere else:

```
dotnet run -- D:\path\to\submissions
```

Each test run has a 5-second limit, so a working submission takes a few seconds and a hanging one up to a few minutes.

### 3. Read the results

Open `results.csv` in Excel (`;` separator, works with Slovak regional settings) or `report.html` in a browser. Anything other than PASS is worth a look in the report or the logs before you take points off.

---

## What the results mean

### Test results

| Result | Meaning |
|---|---|
| `PASS` | The check succeeded. |
| `PASS (no exit)` | Correct answer, but `exit` did not end the program. Counts as a pass. |
| `FAIL` | The check did not succeed. |
| `FAIL (no output)` | The program printed nothing at all. |
| `TIMEOUT` | The program hung and was stopped after 5 seconds. |
| `CHECK: console call` | Crashed on `Console.ReadKey()` / `Console.Clear()`. This happens only because the grader types the input, so check it manually. |
| `NOT RUN` | The program could not be built. |

### Build column

| Build | Meaning |
|---|---|
| `OK` | The student's own project built. |
| `REBUILT: no project file` | No `.csproj`. The grader built the `.cs` files with a standard project; the **setup penalty** is deducted. |
| `REBUILT: project didn't build` | Their project failed but the code itself compiles; the **setup penalty** is deducted. |
| `BUILD FAILED` | The code doesn't compile. 0 points; errors are in `logs/<Name>/build.log`. |
| `NO PROJECT` | No code found at all. |
| `GRADER ERROR` | The grader itself hit an error on this submission (e.g. a file it couldn't read). The other students are still graded; details are in `logs/<Name>/grader-error.log`. |

Results are saved after every student, so if the run stops halfway the students graded so far are kept. If `results.csv` is open in Excel it can't be overwritten: the grader says so and you can close it and run again.

When a repo contains several projects (e.g. class exercises next to the assignment), the grader picks the one whose code mentions the most command names (`"sum"`, `"rect"`, …).

### Code checks (last columns)

These read the source code instead of running it and check the Phase 2 structure: that `sum`, `odm`, `find`, `help`, `rect` each call their own `void` function without parameters, and that `area`, `perimeter`, `diagonal` call a function taking two numbers and returning one. Function names don't matter. `NO BRANCH` means the grader couldn't find where that command is handled.

**Code checks do not affect the points**; they are information for the teacher.

---

## Scoring

Per phase, following the assignment's rubric:

| Points | When |
|---|---|
| 40 | All commands work **and** all edge cases are handled |
| 35 | All commands work |
| 20 | At least one command works |
| 0 | Nothing works, or the program doesn't build |

A command "works" when **all** its test scenarios pass (each command is tested on at least two inputs). Edge cases (marked `[edge]`) only decide between 35 and 40.

**Setup penalty:** if the grader had to rebuild the code with its own project, `setupPenalty` points (default 10) are taken off the total. They appear in the *Setup penalty* column.

### What the edge cases check

- Text instead of a number (`abc`) must be rejected: no crash, and it must not silently become `0`. Asking again or returning to the menu are both fine.
- Square root of a negative number: no crash, no `NaN`.
- Rectangle with a negative, zero or text side must be rejected.
- Array with a trailing space (`50 10 70 40 `) or letters (`50 x 70`), and searching for a missing or non-numeric value.
- Inside `rect` / `array` mode, `exit` may either end the program or return to the main menu; the tests type `exit` twice to allow both.

---

## Changing the tests

Everything you'd want to change is near the top of `Program.cs`, in sections marked with `// ===== NAME =====` banners.

### Settings (`SETTINGS`)

`submissionsDir`, `reposFile`, `resultsFile`, `reportFile`, `logsDir`, `scenarioTimeout`, `buildTimeout`, `setupPenalty`.

### Test scenarios (`FUNCTIONAL: SCENARIOS`)

```csharp
new(1, "sum", "sum 522+3", ["sum", "522", "3", "exit"], Has("525")),
//  |    |        |              |                         |
//  |    |        |              |                         check on the output
//  |    |        |              lines typed into the program, in order
//  |    |        column name in the results
//  |    command group: a command works only if all its scenarios pass
//  phase (1, 2 or 3)
```

Add `Edge: true` at the end for an edge case. Always end the inputs with `exit` (twice inside a mode).

Available checks:

| Check | Passes when |
|---|---|
| `Has("35")` | that number or word appears in the output |
| `HasAny("3", "2")` | at least one of them appears |
| `HasAll("sum", "odm")` | all of them appear |
| `Sequence("10 40 50 70")` | these numbers appear in this order |
| `NoCrash("NaN")` | no crash, something was printed, and none of the listed numbers/words appear |
| `RejectsText("40", "42")` | for text typed as a number: no crash, no `0`, and not `40` without `42` |
| `Found("8", "9")` / `NotFound()` | a position or a found / not-found style answer |
| `AllOf(a, b)` | all the given checks pass |

**Numbers** are compared at the precision the student printed (`8.6023` accepts `8.6`, `8,60`, `8.602325…`, but not `9`); whole numbers must be exact; `13` never counts as `3`. **Words** ignore case and diacritics and must start a word (`nachadz` matches `nachádza` but not `nenachádza`). They also work when Windows garbles Slovak letters in the output.

**Menus and prompts** don't count for number checks, so a `4` in "4. help" can't pass `odm 16`. Before the tests, each program is run once with just `exit` (and once entering `rect` / `array` mode with other numbers) to capture what it prints anyway: those lines, and prompts glued to the next line ("Vyber 1-5: Výsledok: 4"), are ignored when looking for numbers. Word checks still see the whole output. The runs are in `BASELINES` below the scenarios and saved as `logs/<Name>/baseline *.log`; *Printed* in the report still shows the full output.

### Code checks (`STATIC ANALYSIS: CHECKS`)

```csharp
new("P2 area: num(num,num)", "area", "num", ["num", "num"], Depth: 1),
//   column name               command  return  parameter types
```

Types: `void`, `int`, `double`, …, `"num"` = any number type, `"*"` = anything. `Depth: 1` allows the command to reach the function through one helper function.

---

## Known limits: review these manually

- `help lists commands` passes if the command names appear **anywhere** in the output, including a welcome message.
- Number checks ignore the program's menus and prompts (see *Menus and prompts* above), but a number in any other text still counts, e.g. "Zadaj 2 čísla" printed only after typing `sum`.
- The `0` checks for text input can fail a correct program whose error message itself contains a 0 (e.g. "zadaj číslo väčšie ako 0"). Check the log.
- Commit dates come from the student's computer, not from when they pushed.
- The **No AI** rule cannot be checked automatically.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `error CS8802: Only one compilation unit can have top-level statements` when running the grader | The `DefaultItemExcludes` line is missing from `AssistantGrader.csproj` (see Setup). |
| `CodeAnalysis` is not recognized | Run `dotnet add package Microsoft.CodeAnalysis.CSharp`. If `dotnet run` works and only VS Code complains: *Developer: Reload Window*. |
| `No repos.txt found in …` | Expected when you grade folders as they are. Otherwise `repos.txt` is not next to `Program.cs`, or is named `repos.txt.txt`. |
| `FAILED to fetch <Name>` | The repo is private, the URL is wrong, or the folder is checked out at an older commit (expected after `git checkout <hash>`; that version is still graded). |
| A student fails something that looks right | Open `report.html`, click the cell, and compare *Typed* and *Printed*. |

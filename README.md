# AssistantGrader

Automatic grader for the **"Asistent"** assignment in Programming 1 (C# / .NET). It grades every student the same way: it builds each submission, runs it through a fixed set of test scenarios as if a user were typing, checks the code structure, and scores it by the assignment's rubric (3 phases × 40 points).

For every student it produces:

- **`results.csv`**: one row per student with points and every test result (opens directly in Excel).
- **`report.html`**: the same results in a browser. Click any cell to see exactly what was typed and what the program printed. Under each student, a collapsible **Code** section shows every graded `.cs` file with line numbers. Filter to one student, or to only the students that need a manual look.
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

### Review column: what to check by hand

The **Review** column (next to *Total* in `results.csv`, after *Build* in `report.html`) lists what the grader isn't sure about. **An empty cell means the points can be taken as they are.** Otherwise, look at the listed tests before giving the points:

| Reason | What to look at |
|---|---|
| `Answer ignored as menu/prompt` | The test failed, but the expected answer was printed on a line the grader treated as menu or prompt text. Probably a false fail. |
| `One result away from more points` | The phase would get more points (shown as `+5`, `+15`…) if this one test or code check passed. A false fail here costs the most, e.g. a `0` inside an error message. Only listed for phases that already have points. |
| `Code check found no command handler` | `NO BRANCH`: the command may be missing, or handled in a way the grader doesn't recognise. |
| `Crashed on Console.ReadKey/Clear` | Fails only because the grader types the input. |
| `Timeout` | The program may be waiting for something (e.g. "press any key") rather than hanging. |
| `REBUILT …`, `BUILD FAILED`, `NO PROJECT`, `GRADER ERROR` | Setup penalty or 0 points: make sure it's deserved (e.g. the right project was picked). |

An ordinary `FAIL` is not listed: it's a clear result.

In `report.html`, students needing review have a yellow mark by their name. Use the **student dropdown** to show one student at a time (`(review)` marks those needing review) and **Only students needing review** to hide the rest. The two filters work together.

### Test results

| Result | Meaning |
|---|---|
| `PASS` | The check succeeded. |
| `PASS (no exit)` | Correct answer, but `exit` did not end the program. Counts as a pass. |
| `FAIL` | The check did not succeed. |
| `FAIL (no output)` | The program printed nothing at all. |
| `TIMEOUT` | The program hung and was stopped after 5 seconds, or printed more than 1 MB (an endless loop). |
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

Students' programs always run on the newest .NET installed (.NET 10), even when their project targets an older version such as `net8.0`. Without this, a correct program built for .NET 8 would fail every test with "You must install or update .NET".

### Code checks (last columns)

These read the source code instead of running it and check the Phase 2 structure the assignment requires: that `sum`, `odm`, `find`, `help`, `rect` each call their own `void` function without parameters, and that `area`, `perimeter`, `diagonal` call a function taking two numbers and returning one. Function names don't matter.

**They count for Phase 2 points** (see *Scoring*).

`NO BRANCH` means the grader couldn't find where that command is handled. It counts as a fail and is shown in yellow: it is right when the command is missing, but the grader can also miss unusual code (e.g. commands kept in a `Dictionary`), so look at the code before taking points off.

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

**Phase 2 also requires functions.** `area`, `perimeter`, `diagonal` and `rect` work only when their tests pass **and** their code check passes. The code checks for `sum`, `odm`, `find`, `help` count as four more Phase 2 commands. So a student whose rectangle works but computes everything inline gets 20 for Phase 2, not 35 or 40.

**Setup penalty:** if the grader had to rebuild the code with its own project, `setupPenalty` points (default 10) are taken off the total. They appear in the *Setup penalty* column.

### What the edge cases check

- Text instead of a number (`abc`) must be rejected: no crash, and it must not silently become `0`. Asking again or returning to the menu are both fine.
- Square root of a negative number: no crash, no `NaN`.
- Rectangle with a negative, zero or text side must be rejected.
- Unknown command (`xyz`): no crash, and the program must print some message, not just show the menu again.
- Array with a trailing space (`50 10 70 40 `) or letters (`50 x 70`), and searching for a non-numeric value.
- Searching the array for a missing value must answer `-1` or "not found" (a 1-based program printing position `0` fails).
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

Add `Edge: true` at the end for an edge case. Add `AltInputs: [...]` for another input order the assignment allows; the test passes if either order does. Always end the inputs with `exit` (twice inside a mode).

Available checks:

| Check | Passes when |
|---|---|
| `Has("35")` | that number or word appears in the output |
| `HasAny("3", "2")` | at least one of them appears |
| `HasAll("sum", "odm")` | all of them appear |
| `Sequence("10 40 50 70")` | these numbers appear in this order |
| `NoCrash("NaN")` | no crash, something was printed, and none of the listed numbers/words appear |
| `RejectsText("40", "42")` | for text typed as a number: no crash, no `0`, and not `40` without `42` |
| `Found("8", "9")` | one of these positions, or a "found" answer (nachádza, áno, true…); never with a `-1` |
| `NotFound()` | `-1`, or a "not found" answer (nenachádza, nie, false…) |
| `HelpLists("sum", "odm")` | each word is printed more often than after an unknown command |
| `Responds()` | the program printed something besides its menus and prompts |
| `AllOf(a, b)` | all the given checks pass |

**Numbers** are compared at the precision the student printed (`8.6023` accepts `8.6`, `8,60`, `8.602325…`, but not `9`); whole numbers must be exact; `13` never counts as `3`. **Words** ignore case and diacritics and must start a word (`nachadz` matches `nachádza` but not `nenachádza`). Short answer words (`nie`, `not`, `áno`, `true`…) must be the whole word, so `nie` doesn't match `niečo`. Words also work when Windows garbles Slovak letters in the output.

**Labels are not answers:** "Pozícia: -1" is a not-found answer, so `find found` fails on it.

**Input order:** the assignment doesn't say whether `find` asks for the sentence or the substring first, so both Phase 1 `find` tests try both orders and pass if either works. The report shows the order that was used.

**Menus and prompts don't count as answers.** Before the tests, each program is run without being asked anything, to capture what it prints anyway:

- once with just `exit` (the main menu)
- once entering `rect` with other numbers than the tests use (the rect menu)
- for each array test, with that test's own array but no question, so an array the program prints back ("Pole: 50 10 70 40") can't answer `min`
- once with an unknown command, to compare with `help`: a menu printed after every command doesn't count as help

Those lines, and prompts glued to the next line ("Vyber 1-5: Výsledok: 4"), are ignored when checking answers, so a `4` in "4. help" can't pass `odm 16`. `help` and other word checks still see the whole output. The runs are in `BASELINES` below the scenarios and saved as `logs/<Name>/baseline *.log`. In the report, each cell shows both *Printed* (everything) and *Answers checked* (with the menus and prompts removed).

### Code checks (`STATIC ANALYSIS: CHECKS`)

```csharp
new("P2 area: num(num,num)", "area", "num", ["num", "num"], Depth: 1, Phase: 2),
//   column name               command  return  parameter types
```

Types: `void`, `int`, `double`, …, `"num"` = any number type, `"*"` = anything. `Depth: 1` allows the command to reach the function through one helper function. `Phase: 2` makes the check count for that phase's points; leave it out for a column that is only information.

---

## Known limits: review these manually

- `help lists commands` can fail a working help if the program also lists all commands in its "unknown command" message.
- Answer checks ignore the program's menus and prompts (see *Menus and prompts* above), but a number in any other text still counts, e.g. "Zadaj 2 čísla" printed only after typing `sum`.
- A line the program prints in both the test and its baseline is ignored, so a program that prints a result on its own (without being asked) and then again when asked can fail that test.
- Code checks pass when **any** place handling the command calls a matching function: `find` is also a command in array mode, so a matching function there also passes the Phase 2 `find` check.
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

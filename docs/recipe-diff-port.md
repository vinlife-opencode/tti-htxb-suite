# Porting `recipe-diff` to C#

Plan for moving the workbook ↔ machine-recipe comparison out of the Python tool
(`bidfgen recipe-diff`, commit `ff5dcbb`) and into `Htxb.RecipeCore` /
`Htxb.RecipeTool`. Python stays as the reference implementation until parity is
proven on the full delivery, then the Python command is deleted.

## 1. Why

The audit toolchain is C#; the two newest recipe tools are Python, which is the
split-brain the repository rule ("new work in C#") exists to prevent. This one is
small, has no product runtime dependency, and its behaviour is already pinned by
measured expectations, so it is the right first port.

## 2. Target layout

```
src/Htxb.RecipeCore/
  Model/ZadzRecipe.cs        the .zadz JSON model (System.Text.Json, no NuGet)
  Model/ZadzStep.cs          one AgingProcess entry, 22 fields by JSON name
  Diff/RecipeAligner.cs      LCS pairing + alias map
  Diff/DiffResult.cs         pairs / renamed / one-sided / value differences
  Diff/RecipeDiffer.cs       reads workbook (PlanSheetAdapter) + .zadz, compares
  Diff/DiffReport.cs         text and JSON rendering
src/Htxb.RecipeTool/
  Commands/DiffCommand.cs    thin CLI wrapper
tests/Htxb.RecipeCore.Tests/
  DiffTests.cs               unit + the 16-pair baseline (table in §6)
```

## 3. Contract

```
Htxb.RecipeTool diff <workbook.xlsx> <recipe.zadz> [--json] [--strict]
```

Exit codes follow the existing tool: `0` aligned (or differences without
`--strict`), `1` differences with `--strict`, `2` usage/I/O error.

JSON shape — the parity contract with the Python tool:

```json
{
  "recipe": "P_TM_BRF5_B_07_4_32",
  "workbookSteps": 32,
  "machineSteps": 34,
  "pairs": [ { "workbook": "HTGB", "machine": "HTGB1", "how": "alias" } ],
  "renamed": [ ["HTGB", "HTGB1"] ],
  "workbookOnly": [],
  "machineOnly": ["IGSS_-10V_1", "IGSS_-10V_3"],
  "valueDiffs": []
}
```

Numbers are invariant-culture; no trailing zeros added. `--json` writes only the
object so both implementations can be compared structurally.

## 4. Algorithm

1. Read the workbook through `PlanSheetAdapter` (already handles the four layout
   variants, merged cells, `Data Transfer` rows).
2. Read the `.zadz` with `System.Text.Json`; the step names are the JSON property
   `"Test Name"`, the setup is `"Vds(V)"` / `"Vgs(V)"`.
3. Normalise both name lists with `TestName.Normalise` — the C# implementation is
   already documented as mirroring `bidfgen.engine.normalised_test_key`, including
   the trailing `_<n>` collapse.
4. Align positionally with an LCS over the normalised keys (the lists are ≤ 40, so
   a plain dynamic-programming table is enough; no dependency).
5. Inside a replaced block, pair a workbook step with a machine step only through
   the alias map; everything unmatched is `workbookOnly` / `machineOnly`.
6. Compare `Vds`/`Vgs` as numbers (unit and sign tolerant). Limits are **not**
   compared: the machine carries tester pass/fail limits, the workbook carries the
   spec limits used for BiDF binning.

## 5. Shared contract — one alias file, both suites

The alias map must not drift. Add `docs/test-vectors/recipe-aliases.json` to
BidfGen2, asserted by `test_onboard.py` and by the C# suite, the same pattern as
`normalise.json`:

```json
{
  "aliases": {
    "htgb": "htgb1",
    "htrb6r": "htrb6",
    "htgbht6r": "htgbht6",
    "htrb7r": "htrb7",
    "htgbht7r": "htgbht7",
    "htrb8r": "htrb8",
    "htgbht8r": "htgbht8",
    "chrampuphtgb": "htgbramp",
    "htgbrampup": "htgbramp"
  }
}
```

Also add the names this delivery exercises (`HTGB`, `HTGB1`, `HTRB6_R`,
`HTGB_RAMP UP`, `CH RAMP UP  + HTGB`, `IGSS_+20V_1`, `IGSS_-10V_1`) to
`normalise.json`, so a change to either normaliser fails both suites.

## 6. Prerequisite: the `.zadz` files must be reachable

The 16 machine recipes (~215 KB) are only in the untracked `Samples\` drop today.
Pin them under
`handover\fixtures\samples\TTSZ Zhou Rong 261029 New Recipes\...\2108 Recipe 261031\`
so a fresh clone and the C# tests can run, exactly as the 17 workbooks were pinned
in `f91f821`. `test-recipetool.ps1` then resolves them the same way.

## 7. Expected baseline (measured with the Python tool, 2026-10-31)

| recipe | paired | exact | alias | machine-only |
|---|---|---|---|---|
| BR9M, BRIC, BRIG, BRJJ, BRJO | 32 | 25 | 7 | — |
| BRF5, BRF9, BRFC, BRI9, BRJ7 | 32 | 25 | 7 | `IGSS_-10V_1`, `IGSS_-10V_3` |
| all six SQ | 15 | 14 | 1 | `RAMP_DOWN` |

Every recipe reports `RESULT: differences above`; none is aligned. The C# port
must reproduce this table exactly, including the seven rename pairs and the
`CH RAMP UP  + HTGB` / `HTGB_RAMP UP` → `HTGB_RAMP` pairing.

## 8. Tests

**Unit (no files):** alias pairing; numbered variants (`IGSS_20V_1/2/3`) pair in
order rather than collapsing; a machine-only step; a workbook-only step; the two
SQ ramp spellings.

**Real data (16 pairs):** the table in §7 asserted per recipe, not just in total.

**Parity:** a script that runs both implementations with `--json` over all 16
pairs and compares the objects; added to `test-recipetool.ps1` as a phase that
skips visibly when the Python tool or the samples are absent. It becomes the
retirement proof for the Python command.

## 9. Steps

| # | step | output | estimate |
|---|---|---|---|
| 1 | `ZadzStep` / `ZadzRecipe` + reader | Core model + JSON round-trip test | 2 h |
| 2 | `RecipeAligner` + alias file | Core + unit tests | 3 h |
| 3 | `RecipeDiffer`, text/JSON report | Core | 3 h |
| 4 | `DiffCommand` + usage text | Tool | 1 h |
| 5 | Pin `.zadz`, add vectors, 16-pair baseline | Tests | 2 h |
| 6 | Parity script + `test-recipetool.ps1` phase | verification | 2 h |
| 7 | Docs (`PROJECT_LOG.md`, `docs/std-ingest.md` cross-reference), delete the Python command | docs | 1 h |

Roughly **1.5 working days**, with steps 1–4 independent of the data work.

## 10. Risks

| risk | mitigation |
|---|---|
| normalisation drifts between the two implementations | shared `normalise.json` vectors, asserted by both |
| alias map drifts | single `recipe-aliases.json`, asserted by both |
| number formatting (`930` vs `930.0`) differs | compare parsed doubles, and in `--json` emit raw strings as the Python tool does |
| `.zadz` is UTF-8 with BOM | `File.ReadAllText` + `JsonSerializerOptions` tolerate it; add a fixture test |
| a future delivery changes the `.zadz` schema | unknown `AgingProcess` fields are ignored; a missing `Test Name` fails loudly |

## 11. Done criteria

- `Htxb.RecipeTool diff` reproduces the §7 table for all 16 pairs.
- C# suite: the six unit checks plus 16 real-data checks, all green in
  `test-recipetool.ps1`.
- Parity script green against the Python tool on the same 16 pairs.
- The Python `recipe-diff` command is deleted in a separate commit after two green
  runs, with `docs/std-ingest.md` pointing at the C# command instead.

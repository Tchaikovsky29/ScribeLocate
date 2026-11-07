# ScribeLocate

C# Selenium test for the **First Name**, **Gender**, and **State** fields on the [CloudQA practice form](https://app.cloudqa.io/home/AutomationPracticeForm).

## Requirements and setup

Use a Bash shell with Firefox, `curl`, and network access for the first setup. Run:

```bash
bash setup.sh
```

This installs .NET 10 into `.tools/dotnet` and restores `Selenium.WebDriver` and `Selenium.Support`. The run scripts use that local SDK. Selenium Manager obtains a compatible Firefox driver on first run. The SDK, packages, and build output stay under the repository's ignored `.tools/`, `bin/`, and `obj/` directories.

## Run

```bash
bash run.sh \
  --url https://app.cloudqa.io/home/AutomationPracticeForm \
  --first-name Ada --gender Female --state India
```

The URL and values are configurable. `--form-heading "Other form"` explicitly targets a form by its visible heading when needed. `--headed` shows the browser in an active desktop session; headless is the default. The scripts preserve an existing `XDG_RUNTIME_DIR` so headed Firefox can use the desktop's display server. A successful run prints the selected `TARGET` path and three `PASS` lines, then exits with code 0. Each failed field prints its own `FAIL` line; a missing or ambiguous target form reports all three as failed. Any failure exits nonzero.

To watch the test, add both optional delays:

```bash
bash run.sh --headed --field-delay-ms 1000 --keep-open-seconds 10
```

`--field-delay-ms` waits that many milliseconds **between** First Name and Gender, then between Gender and State. `--keep-open-seconds` leaves the browser open for that many seconds **after** the results print, including when a field fails. Both default to `0`; the keep-open value accepts up to 86400 seconds. These are viewing delays and do not change the control wait or retry timeouts.

If `--headed` cannot start Firefox, check that the command is running inside a graphical desktop; omit `--headed` for SSH or other sessions without a display. A `NU1900` warning means NuGet could not fetch vulnerability advisory data. It does not cause a browser startup failure, and the run can still succeed if the dependencies are already restored.

To check a modified local page from the repository root:

```bash
bash run.sh --url "file://$(pwd)/tests/fixtures/changed-form.html"
```

For a form with a different visible heading, add `--form-heading "Nested Target"` (or that page's actual heading). The defaults are `Ada`, `Female`, and `India`; the URL defaults to the live CloudQA page.

## How field matching works

The explorer prefers one visible top-level form with the heading **Form**. If no top-level form has the requested heading, it walks child iframes and open shadow roots, including nested combinations. It records the selected context path, inventories slot assignments, and bounds traversal to 10 levels and 96 contexts. Repeated ancestor URLs are skipped so the page's self-embedding iframe cannot cause an endless search. Among matching forms in nested contexts, the shallowest unique form wins; equal-depth matches fail as ambiguous.

Within the chosen form, the resolver walks the rendered tree, including open shadow roots and named slot assignments. It connects exact visible labels to controls through `for`, wrapping labels, or the nearest field container. It also accepts `aria-label`, `aria-labelledby`, and a radio group's fieldset legend or accessible group name. Radio choices use their value, accessible name, or adjacent visible text. Each field action finds the form again, waits up to 12 seconds for a usable control, and retries transient stale-element or interaction failures. Assertions use a freshly resolved control. Equal-strength matches fail with candidate paths and reasons; field lookup does not rely on the page's original IDs, names, classes, field order, or a full DOM path.

The selectors in the explorer enumerate broad element types such as forms and frames. Field selection uses the label, accessible name, control type, radio option, and composed form context. No original ID, class, fixed position, or full XPath is required. The site's shadow forms place labels in shadow roots and controls in named light-DOM slots; the resolver follows those assignments.

## Test assumptions

The task names three fields but does not specify validation rules or a submission result. The project defines a successful test as an interaction with the intended form followed by an observable assertion:

| Field | Action | Assertion |
| --- | --- | --- |
| First Name | Enter the configured text | The field's value equals that text |
| Gender | Choose the configured radio option | That option is selected |
| State | Choose the configured non-placeholder option | The selected option equals the requested option |

The project does not infer rules for valid names, required-field messages, or server-side behavior. It does not submit the form because the task provides no expected submission outcome.

The page contains copies of similar fields in other forms and browsing contexts. The project tests one intended logical form. The ordinary top-level form headed **Form** is the default target. A self-contained form moved into an iframe or open shadow root is discoverable. When multiple forms remain equally plausible at the same context depth, the test fails with an ambiguity report rather than choosing by document order. `--form-heading` resolves forms with different headings; equally named, equally scoped forms remain ambiguous.

The project assumes the three fields retain enough user-facing meaning to identify them, such as labels or accessible names, and that Gender remains a radio group and State remains a native dropdown. Changes to position, IDs, names, classes, and surrounding wrappers are in scope. If both meaning and distinguishing context disappear, reliable identification is impossible.

## Supported boundaries

- Open shadow roots, named slots, and accessible iframes are supported. Closed shadow roots and custom dropdown widgets need a different integration.
- Discovery is bounded to 10 context levels and 96 contexts. An iframe whose URL repeats an ancestor URL is skipped to prevent the live page's self-embedding loop; a target inside such a frame cannot be reached by this search.
- A single shallow form with the preferred heading wins. If a modified page leaves a shallow decoy with that heading and moves the intended form deeper, supply a distinguishing `--form-heading` or another explicit targeting rule. Equal-depth matches fail as ambiguous.
- Controls must become visible and usable within 12 seconds per field. Repeated replacement beyond that limit fails with the last observed reason. State must remain a native `select`, and a requested option must be enabled, have a nonempty value, and not use common placeholder wording.

The [approach and limitations](docs/approach.md) provide a short summary. The [implementation plan](docs/implementation-plan.md) records the design steps and verification criteria.

## Verification

Run all local variants with:

```bash
bash tests/run-resilience.sh
```

The suite checks 12 positive and negative cases, including reordered fields, changed identifiers and wrappers, arbitrary named slots, similar fields and forms, accessible names, a nested shadow form, an iframe inside a shadow root, delayed and replaced controls, configurable viewing delays, missing or ambiguous targets, placeholder State selection, and an invalid delay. It checks exit codes and expected field results. All 12 cases passed on 2026-11-07. The live ordinary form, six-level shadow form, and nested iframe form also passed on that date.

Individual fixtures can be run by passing an absolute `file://` URL to `--url`. Use `--form-heading "Nested Target"` or `--form-heading "Frame Target"` for the two context variants in `tests/fixtures/context-forms.html`.

The entry point and interaction checks are in [`Program.cs`](src/ScribeLocate/Program.cs). [`ContextExplorer.cs`](src/ScribeLocate/ContextExplorer.cs) traverses frames and shadow roots; [`SemanticForm.cs`](src/ScribeLocate/SemanticForm.cs) resolves fields. The fixtures and suite are under [`tests/`](tests/).

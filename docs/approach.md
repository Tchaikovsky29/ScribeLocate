# Execution flow and limitations

1. **Open the page.** ScribeLocate starts Firefox and navigates to `--url` (the CloudQA practice page by default). It waits up to 20 seconds for a target form.
2. **Choose one form.** The default target is the visible top-level form headed **Form**. `--form-heading` requests a different heading. If needed, the explorer searches iframes and open shadow roots, including nested ones. When no heading is supplied, a form containing all three field labels can be a fallback. It chooses the shallowest unique match; equally scoped matches fail as ambiguous.
3. **Find each field within that form.** Before each action, the resolver follows the rendered tree through open shadow roots and named slots. It matches exact visible labels, label associations, supported ARIA names, and the expected control type. Gender also requires the requested radio option, matched by value or visible option text. If two controls have equally strong evidence, that field fails with both candidate paths. Original IDs, classes, field order, and fixed XPaths are not required.
4. **Act and verify.** First Name is cleared and filled, then its value is compared with the requested text. The requested Gender radio is clicked, then checked for selection. State is chosen from a native dropdown, then its selected text is compared with the requested option. Disabled, empty-value, and common placeholder State options fail. The control is resolved again for each assertion. Missing or replaced controls are retried for up to 12 seconds per field.
5. **Report the result.** Each field prints `PASS` or `FAIL`. One field failing does not stop the other field checks. The process exits with code `0` only when all three pass; a missing or ambiguous target form fails all three. Optional CLI delays can pause between fields and keep Firefox open after results. The form is not submitted because the task defines no expected submission result.

## Limitations

- Fields need meaningful labels or accessible names. If that information disappears, the intended control cannot be identified reliably.
- Gender must remain a radio group and State a native `select`; custom widgets need new adapters. Closed shadow roots are inaccessible.
- Discovery stops at 10 context levels or 96 contexts and skips iframes whose URL repeats an ancestor URL.
- A shallow decoy with the preferred heading may be chosen before a deeper intended form. That situation needs a distinguishing heading or another explicit targeting rule.

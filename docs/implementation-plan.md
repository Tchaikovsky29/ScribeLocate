# Implementation plan

## Goal

Given a configurable page URL, find the intended First Name, Gender, and State controls; interact with them; and verify their resulting states across modified versions of the CloudQA practice page. Discovery may inspect many candidates, but the test acts on exactly one unambiguous logical form.

The [README test assumptions](../README.md#test-assumptions) define the observable behavior and the limits of this promise.

## Current baseline

The C# Selenium program passes against the live ordinary and shadow forms (including six shadow levels), a changed-page fixture, a two-frame live form, and fixture forms inside nested open shadow roots and a shadow-hosted iframe. It resolves labels across named slots, waits for usable controls, and retries after a DOM replacement during an action. Optional CLI delays pause between fields or keep the browser open after results. The local resilience suite covers 12 positive and negative cases and passes. The README contains setup, run commands, matching rules, evidence, and limits.

## Steps

1. **Define the target and result contract .** Keep URL and chosen values configurable. Default to the ordinary form identified by its visible context. Use `--form-heading` to select a differently titled form. Report the chosen context path and a separate pass/fail result for each field; return a nonzero exit code on missing, ambiguous, or incorrect results. Equally scoped forms with the same heading fail as ambiguous.
2. **Discover browsing contexts .** Walk the main document, child iframes, open shadow roots, and nested combinations. Keep a path through frame and shadow hosts for each candidate. Inventory named slot assignments. Bound depth and work, detect repeated ancestor URLs so the page's self-embedding shadow iframe cannot cause unbounded traversal, and restore the previous frame after each branch.
3. **Resolve the logical form and fields .** Prefer exact visible labels and HTML label associations or accessible names; use control type, radio-option text, form context, and named slot assignments to distinguish candidates. Re-evaluate the form before each field action rather than rely on one cached form reference. Do not use document order or a single generated XPath as a tie-breaker. Fail with candidate paths and reasons when confidence is insufficient.
4. **Exercise and assert .** Wait for each control to become present and usable, retrying after transient stale-element and interaction failures. Enter First Name and compare its value; select Gender and check the requested radio; select a non-placeholder State option and check the selected text. Re-resolve before asserting so a replaced control can still be checked. Do not submit the form without an expected submission outcome.
5. **Verify resilience and safety .** Run `bash tests/run-resilience.sh` against 12 local cases: reordered fields, changed identifiers and wrappers, arbitrary named slots, lookalike fields and forms, iframe and nested open shadow roots, delayed and replaced controls, configurable viewing delays, ambiguous forms and fields, a missing State despite a Country decoy, placeholder State selection, and an invalid delay. The suite asserts exit status and field results. The live ordinary, six-level shadow, and nested iframe forms also pass manual checks.
6. **Finish the submission .** The README documents setup, sample commands, matching rules, supported context boundaries, test evidence, and limits such as closed shadow roots or custom dropdown widgets. The solution uses C# and Selenium.

## Done when

- All three checks pass on the original page and the supported changed-page variants.
- Missing or ambiguous targets fail with useful diagnostics and a nonzero exit code.
- The configurable URL works for both the live page and local fixtures.
- The documentation states exactly what the tests prove and what changes remain outside scope.

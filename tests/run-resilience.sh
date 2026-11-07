#!/usr/bin/env bash
set -uo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_HOME="$project_root/.tools/cli-home"
export NUGET_PACKAGES="$project_root/.tools/nuget"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-$project_root/.tools/runtime}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet="$project_root/.tools/dotnet/dotnet"
dll="$project_root/src/ScribeLocate/bin/Debug/net10.0/ScribeLocate.dll"

if ! "$dotnet" build "$project_root/src/ScribeLocate/ScribeLocate.csproj" --no-restore --nologo -v:q; then
  exit 1
fi

passed=0
run_case() {
  local name="$1" fixture="$2" expected_status="$3" expected_text="$4"
  shift 4
  local output status=0
  output="$("$dotnet" "$dll" --url "file://$project_root/tests/fixtures/$fixture" "$@" 2>&1)" || status=$?

  if [[ "$status" -ne "$expected_status" || "$output" != *"$expected_text"* ]]; then
    printf 'FAIL %s (exit %s, expected %s)\n%s\n' "$name" "$status" "$expected_status" "$output"
    exit 1
  fi
  if [[ "$expected_status" -eq 0 ]]; then
    for field in 'First Name' Gender State; do
      if [[ "$output" != *"PASS $field"* ]]; then
        printf 'FAIL %s (missing PASS %s)\n%s\n' "$name" "$field" "$output"
        exit 1
      fi
    done
  fi
  printf 'PASS %s\n' "$name"
  passed=$((passed + 1))
}

run_case 'reordered fields and decoy form' changed-form.html 0 'TARGET document > form[heading="Form"]'
run_case 'accessible names and fieldset' semantic-names.html 0 'PASS State'
run_case 'arbitrary named slots and decoy fields' mutated-slots.html 0 'PASS State' \
  --first-name Grace --gender Female --state Canada
run_case 'nested open shadow roots' context-forms.html 0 'shadow:inner-form' \
  --form-heading 'Nested Target'
run_case 'iframe inside shadow root' context-forms.html 0 'iframe[1, src="srcdoc"]' \
  --form-heading 'Frame Target'
run_case 'delayed and replaced controls' delayed-rerender.html 0 'PASS State'
run_case 'configurable viewing delays' changed-form.html 0 'KEEP OPEN Browser remains open for 1 second(s)' \
  --field-delay-ms 30 --keep-open-seconds 1
run_case 'ambiguous forms fail' ambiguous-forms.html 1 "2 top-level forms have heading 'Form'"
run_case 'ambiguous field fails' ambiguous-field.html 1 'FAIL First Name: First Name: ambiguous 2 candidates'
run_case 'missing State fails despite Country decoy' missing-state.html 1 'FAIL State: State did not pass within 12 seconds'
run_case 'placeholder State fails' changed-form.html 1 "FAIL State: State option 'Choose' is a placeholder or disabled" \
  --state Choose
run_case 'invalid delay fails before browser startup' changed-form.html 2 \
  'ARGUMENT ERROR: --field-delay-ms must be a nonnegative integer' --field-delay-ms -1

printf 'All %s resilience cases passed.\n' "$passed"

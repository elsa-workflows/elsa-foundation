# Automatic Double to String bindings

**Status**: Implemented

An author selects a reusable activity's Double output for Write Line.Text and publishes the caller. Write Line receives the invariant round-trip text. A second binding to the original output receives the Double. The acceptance example is 19.25 on a Dutch worker.

## Acceptance

- The existing selected-source inspector identifies an automatic conversion for a known matching pair. Selecting None shows the exact-contract rejection beside the input. Selecting Auto again clears that rejection. Existing or unavailable preview data must never describe a different binding.
- Preflight and publication select the same operation for an unschematized, scalar typed Double and a plain String. None requires identical contracts. Previous executable plans retain their interpretation.
- Runtime preserves null, absence and protection policy. Invalid present payloads fail conversion. Automatic conversion does not parse text or serialize arbitrary objects.
- Verify the reference inspector in both themes and narrow and desktop views, and inspect the published caller's committed Text input and numeric output.

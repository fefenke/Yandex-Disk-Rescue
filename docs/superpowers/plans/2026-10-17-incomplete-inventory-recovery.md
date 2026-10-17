# Incomplete cloud inventory recovery

Scope: only the confirmed inventory-completeness bug in 1.1.1.

1. Add failing tests for a folder error, cached retry, retained successful folders, legacy error-bearing inventories, cancellation and metadata refresh preserving incomplete checkpoints.
2. Persist unresolved folder paths with each checkpoint, resume them once per scan from their first page, mark completion only with no unresolved work, and rebuild legacy error-bearing lists from the cloud root while retaining local downloads/history. Metadata-only saves must retain checkpoint state. Automatic passes must not stop solely because an incomplete list produced an empty download queue; back off when only enumeration errors remain.
3. Run targeted enumeration, interrupted-page and pass-transition tests; independent review; build and run full x64/x86 offline checks. Produce a clean test copy. Publishing a further release is separate from implementing this fix.

No preview rescue, checksum policy, platform migration or unrelated interface changes.

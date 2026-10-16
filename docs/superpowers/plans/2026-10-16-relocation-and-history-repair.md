# Portable resume and bounded history implementation plan

> For agentic workers: use executing-plans or subagent-driven-development, verifying each task before integration.

Goal: moving the application with its data must retain validated download parts and archive ownership. Repeated passes must not grow the state journal without bound. Previously downloaded user files must remain untouched.

1. Reproduce relocation using a legacy absolute-path map and confirmed bytes; reject different account/root or cloud version and corrupt extents. Normalize new maps to stable history-context IDs, accept legacy maps only within the same context/version, retain hash verification. Persist archive ownership across moves. Files: ApiMappedDownloads, RangeMap, FilePreserver, StartupReliabilityTests.
2. Compact history only when redundancy/size justify it, retaining the latest complete record for every path and its integrity/resume evidence. Flush a temporary file before atomic replacement; failed replacement leaves the original intact. Rebuild the derived index. Keep append writes during a pass. Dedicated history compaction tests and implementation are isolated from task 1.
3. With the user's application closed, inspect owned archived payload/map pairs read-only first; restore compatible confirmed bytes without overwriting newer valid progress. Remove only proven empty program-owned service artifacts, retaining uncertain/nonempty copies. Never publish local diagnostics.
4. Run full x64/x86 tests, review, update user guides/release notes, audit source and ZIP payloads for private data, publish the verified corrected release and validate downloaded public packages. No broad cleanup of user reports or account data.

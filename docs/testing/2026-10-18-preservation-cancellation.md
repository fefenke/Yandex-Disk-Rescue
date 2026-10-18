# Cancellation during file preservation — 2026-10-18

Task 3 of the approved Windows stabilization plan. The preservation journal, archive names, and recovery format are unchanged.

## Behavior

- `ApiEngine.FinishPayload` passes its stop token to `FilePreserver.Complete`.
- Verification of an existing destination uses that token. Fingerprinting a new payload or comparing an archived duplicate reads in 1 MiB chunks and checks cancellation between reads. The verification methods accept a progress callback.
- Cancellation is checked again before writing a preservation intent or deleting a redundant payload. Once an intent is durable, the move and ledger update run as the existing recovery transaction without new cancellation points. A subsequent pass can recover an interrupted transaction from that intent.
- The three-argument `Complete` overload remains for existing internal tests and uses no cancellation token. Recovery compares files with the no-token fingerprint overload because it must finish or keep the pending intent.

## Verification

Before implementation, `PreserverCancellationTests.Existing` failed: `File preservation has no cancellable verification boundary.`

Focused tests passed for cancellation after reading started in the old destination, the new payload fingerprint, and duplicate archived copy comparison. Each verifies source bytes remain and no intent was written. A fresh `FilePreserver` retries successfully; identical failed copies deduplicate. Existing `SafeDownloadTests.Run` passed, including recovery from intents at four interruption stages and protection of pending operations.

Full x64 and x86 suites passed with exit code 0 in the ordinary Windows session. Their results are stored in `preservation-2026-10-18/`.

## Limit

The intent and move sequence deliberately finishes without cancellation once it starts. On a filesystem that must recompare an already existing target, that final recovery-critical comparison may still take time. The stop button does not preempt an in-progress disk read. Tests cover cancellation between chunks and crash recovery fixtures, but do not simulate physical power loss.

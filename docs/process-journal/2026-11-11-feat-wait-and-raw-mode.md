2026-11-11 · gate · LongRunningWaitShould · Two wait tests ran on a FakeTimeProvider nothing advanced; under a mutation that made them poll they hung the mutation run instead of failing, and the run was only recovered by a per-mutation timeout. (self)

2026-11-11 · gate · Git Bash · `vion api GET /me/longRunningOperations` from the Bash tool answered 404: MSYS path conversion rewrote the path argument to `C:/Program Files/Git/me/...` before vion saw it; the live check needed `MSYS_NO_PATHCONV=1`. (self)

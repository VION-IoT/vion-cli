2026-11-27 · gate · round trip · Git Bash rewrote the container path in `docker create … sh /roundtrip.sh` to `C:/Program Files/Git/roundtrip.sh`, so the container failed to start; the run needed MSYS_NO_PATHCONV=1. (self)

2026-11-27 · gate · round trip · A non-AOT single-file win-x64 publish, the one Windows build this desk links, fails restore in locked mode (NU1004: the lock file carries the ILCompiler package PublishAot=false drops); it ran from an archive copy of the tree with RestoreLockedMode=false, so the worktree's lock file stayed as committed. (self)

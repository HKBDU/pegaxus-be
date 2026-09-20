# RTK - Rust Token Killer (Codex CLI & Antigravity)

**Usage**: Token-optimized CLI proxy for shell commands.

## Rule

Always prefix shell commands with `rtk`.

Examples (.NET & Backend):

```bash
rtk dotnet build
rtk dotnet test
rtk dotnet run
rtk dotnet restore
rtk docker compose ps
rtk git status
rtk git diff
```

## Meta Commands

```bash
rtk gain            # Token savings analytics
rtk gain --history  # Recent command savings history
rtk proxy <cmd>     # Run raw command without filtering
```

## Verification

```bash
rtk --version
rtk gain
which rtk
```

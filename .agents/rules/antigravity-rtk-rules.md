# RTK - Rust Token Killer (Google Antigravity)

**Usage**: Token-optimized CLI proxy for shell commands.

## Rule

Always prefix shell commands with `rtk` to minimize token consumption.

Examples (.NET & Backend):

```bash
rtk dotnet build
rtk dotnet test
rtk dotnet run
rtk dotnet restore
rtk docker compose ps
rtk git status
rtk ls Pegaxus.API/
rtk grep "pattern" Pegaxus.Service/
```

## Meta Commands

```bash
rtk gain              # Show token savings
rtk gain --history    # Command history with savings
rtk discover          # Find missed RTK opportunities
rtk proxy <cmd>       # Run raw (no filtering, for debugging)
```

## Why

RTK filters and compresses command output before it reaches the LLM context, cutting up to 90% of the bash output on common operations. Always use `rtk <cmd>` instead of raw commands.

@RTK.md
@HEADROOM.md
@CAVEMAN.md
@PONYTAIL.md
@CODING_CONVENTIONS.md
 
@.agents/skills/caveman/SKILL.md

# AGENTS.md

**Usage**: Context routing for Codex-powered AI agents.

## File Structure

- `AGENTS.md`: Global routing rules
- `<agent>.md`: Agent-specific instructions
- `skills/<skill>/SKILL.md`: Reusable skill instructions

## Project Documentation

| File | Load | Purpose |
|------|------|---------|
| `CODING_CONVENTIONS.md` | Always (`@` above) | Architecture, module layout, DB/transaction rules, API and test conventions. New code must follow it. |
| `REFACTOR_LOG.md` | On demand (read it, do not `@`-load) | History of the `feat/refactor-system` refactor: what changed, why, and what was deliberately left alone. |

## Routing Rules

| Prefix | Behavior |
|--------|----------|
| `@`    | Load file as context |
| `!`    | Load file as executable prompt |
| `##`   | Load file as tool definition |

### File Location

Files are searched in this order:

1. Current directory
2. `.agents/`
3. `.agents/skills/<skill>/`

## Example

```markdown
# AGENTS.md

@RTK.md
@knowledge-base.md

## Code Agent

@.agents/skills/caveman/SKILL.md
```
## Agent skills

### Issue tracker

Issues live on GitHub (`HKBDU/pegaxus-be`), accessed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Categories: `bug`, `feature`, `refactor`. States: `needs-triage`, `needs-info`, `ready` (agent-ready), `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

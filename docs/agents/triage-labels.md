# Triage Labels

The skills speak in terms of canonical triage roles. This file maps those roles to the label strings used in this repo's issue tracker (GitHub, `HKBDU/pegaxus-be`).

Every triaged issue carries exactly one category label and one state label.

## Categories

| Label in mattpocock/skills | Label in our tracker | Meaning                                         |
| -------------------------- | -------------------- | ----------------------------------------------- |
| `bug`                      | `bug`                | Something is broken                             |
| `enhancement`              | `feature`            | New feature or improvement                      |
| _(repo-specific)_          | `refactor`           | Structure change with no behavior change        |

## States

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready`              | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from these tables.

Create any missing label with `gh label create <name>`.

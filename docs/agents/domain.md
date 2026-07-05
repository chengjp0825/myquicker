# Domain docs

## Layout

Single-context.

- `CONTEXT.md` at the repo root — project domain language, boundaries, and consumer rules.
- `docs/adr/` at the repo root — architectural decision records.

## Consumer rules

When a skill asks for domain context, read `CONTEXT.md` first, then the relevant ADRs in `docs/adr/`.

- If `CONTEXT.md` does not exist yet, create it from the project README, `CLAUDE.md`, and `SPEC.md`.
- If `docs/adr/` does not exist yet, create it when the first ADR is needed.
- Keep `CONTEXT.md` focused on domain concepts and constraints, not implementation details.

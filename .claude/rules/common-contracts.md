---
paths:
  - "Sandbox.Common/**"
---

# Sandbox.Common: public contracts

- This project defines **what** the system does, not **how**. Every implementation project depends on it, so any change here affects all of them and the common integration test suite.
- Commands (`Commands/`) are immutable `record`s that implement `ICommand`. They carry only the data a caller provides. Never put events, aggregate state, or anything specific to one strategy in them.
- Read models (`Readmodels/`) are the shapes queried by callers and by the common test suite. Each implementation maintains them **in memory**, so don't add persistence concerns to them.
- Never reference an implementation project (`Sandbox.Aggregates`, ...) from here.
- When you add or change a contract, update the common integration test suite to match, then make sure every implementation still passes it.

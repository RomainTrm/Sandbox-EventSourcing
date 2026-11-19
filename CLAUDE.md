# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Goal

Implement the **same business problem** several times, once per CQRS/ES strategy, to compare them. The business rules are in `README.md`: stores buy and sell items to customers through orders, each store sells only from its own inventory, and an item moving between inventories gets a new identity that keeps the old one for traceability.

## Solution layout

- **`Sandbox.Common`** holds the **public contracts** that every implementation shares: commands (`Commands/`, implementing `ICommand`), read models (`Readmodels/`), and the database infrastructure (`Database/`). It must stay strategy-agnostic, so never put strategy-specific types here.
- **One project per strategy**, for example `Sandbox.Aggregates` (classical aggregates) and later a DCB implementation. Each one implements the commands and read models from `Sandbox.Common` in its own style. Each has its own rules file under `.claude/rules/` that is scoped to its folder. **Follow that file when working in the project.** A new strategy gets a new project in the `/Implemetations/` solution folder of `Sandbox-EventSourcing.slnx` (the folder name is misspelled) and a new rules file.
- **`Sandbox.Updater`** is a console app that applies SQL migrations to Postgres.
- **Tests**: every implementation must pass a **common integration test suite** that is written against the public contracts only. Each implementation's test project runs that suite against its own implementation. Tests specific to one implementation's internals (like `Sandbox.Aggregates.Tests/EventStoreTests.cs`) go in that implementation's test project.

## Cross-cutting design decisions

- **Read models are kept in memory.** They are not what this project is comparing, so only events are persisted in Postgres. Don't add read model tables, projections stored in the database, or projection checkpoints.
- Persistence goes through `Sandbox.Common.Database` (`IConnection` / `ITransactionConnection`, which wrap Dapper and Npgsql). Parameters are passed as `Dictionary<string, object>`.
- `ImplicitRollbackTransactionConnectionFactory` returns a connection that is already inside a transaction and is never committed, so disposing it rolls everything back. Tests use it for isolation, and they also use random tenant and aggregate IDs.

## Commands

The solution targets **.NET 11** (`net11.0`) and uses new C# features such as `closed record`.

```sh
make start-database                     # Postgres 17 on 127.0.0.1:5432 (postgres/postgres) and Adminer on :8381
dotnet run --project Sandbox.Updater    # apply pending migrations; required before running tests
dotnet build Sandbox-EventSourcing.slnx
dotnet test Sandbox-EventSourcing.slnx
dotnet test Sandbox.Aggregates.Tests --filter "FullyQualifiedName~EventStoreTests.SaveEvents_WithStaleVersion_Throws"
```

- The tests are integration tests: they need the database running and migrated.
- The updater catches every exception and only prints it, so a failed run still exits with code 0. Check its output for `Succeeded`.

## Migrations

- To add a migration, create `Sandbox.Updater/Migrations/<name>.sql` and append `"<name>"` to `UpgradeSteps.All`. Order matters.
- All pending steps run in one transaction and are recorded in `__migration`.
- Name each implementation's tables after its strategy (for example `aggregates_events`) so the implementations don't collide.
- Watch out for the misleading name in `SqlQueries.MigrationDoesNotExists()`: it returns `true` when the table exists.

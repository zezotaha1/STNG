# STNG repository instructions

Follow the applicable user and global Codex instructions together with the repository's established ABP and Clean Architecture conventions.

Before creating or modifying an ABP Domain Entity in STNG, load the installed `dotnet-ddd` skill. Classify the type as an aggregate root, child entity, immutable append-only record or value object before selecting an auditing base. Re-evaluate that decision when its lifecycle changes, and verify soft-delete filters, identity and aggregate relationships, authorization behavior, migrations and tests. Do not treat full auditing as Entity Change Tracking/history or blanket-upgrade unrelated entities.

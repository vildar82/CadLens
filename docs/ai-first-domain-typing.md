# AI-First Domain Typing Experiment

## Status

This document describes an experiment, not the current CAD Lens architecture. Apply it only to a deliberately selected slice after its scope has been agreed. Do not retrofit the entire solution.

## Hypothesis

An AI coding agent may be probabilistic, but the system around it can reject many incorrect decisions deterministically.

The experiment asks whether stronger domain types make invalid code difficult or impossible to compile. The goal is not merely to make code pleasant to read. The goal is to reduce the number of incorrect programs that an agent can express.

```text
Agent output -> Type system -> Compiler -> Tests -> Host verification
                    |
                    +-> reject invalid combinations early
```

Compiler checks complement tests and native AutoCAD verification. They do not replace either one.

## Core instruction

For the selected experimental slice, do not expose primitive values at domain boundaries when different values with the same primitive representation have different meanings.

Replace ambiguous primitives with small domain types that express:

- identity, such as `LayerId` or `DrawingId`;
- units, such as `Distance`, `Area`, or `Angle`;
- coordinate spaces, such as `WorldPoint` or `ViewportPoint`;
- validated states, such as `PositiveDistance` or `ClosedBoundary`;
- domain outcomes, when failure is an expected state rather than an exception.

Use the type system to prevent invalid composition. Do not rely on parameter names, comments, or an agent remembering a convention that the compiler can enforce.

## Rules

### 1. Type by meaning, not by storage

Values with different domain meanings must use different types even when they contain the same primitive value.

```csharp
public readonly record struct LayerId(string Value);
public readonly record struct DrawingId(Guid Value);
public readonly record struct Distance(double Meters);
public readonly record struct Angle(double Radians);
```

An API should communicate its contract through those types:

```csharp
public void Move(EntityId entity, Distance distance, Angle angle);
```

Swapping `distance` and `angle` must be a compiler error rather than a review concern.

### 2. Keep units explicit

Do not pass a bare `double` when its unit matters. Use one canonical internal unit for each quantity and name it in the representation.

Conversions between units must be explicit and centralized:

```csharp
Distance distance = Distance.FromMillimeters(250);
double meters = distance.Meters;
```

Do not add values expressed in different units before conversion. Do not encode unit information only in variable names such as `lengthMm`.

### 3. Separate coordinate spaces

Points and vectors from different coordinate systems must not share one public domain type.

```csharp
public readonly record struct WorldPoint(double X, double Y, double Z);
public readonly record struct ViewportPoint(double X, double Y);
```

Transformations between spaces must be named operations. Avoid implicit conversions.

### 4. Make validated states unrepresentable until validated

When an operation requires a value with a proven property, accept a type that represents that property instead of repeatedly checking a general value.

```csharp
public sealed record ClosedBoundary
{
    private ClosedBoundary(PolylineSnapshot polyline)
    {
        Polyline = polyline;
    }

    public PolylineSnapshot Polyline { get; }

    public static HostResult<ClosedBoundary> TryCreate(PolylineSnapshot polyline)
    {
        // Validate once, then create the stronger state.
    }
}
```

C# cannot prove arbitrary geometry invariants at compile time. The compiler can ensure that only a `ClosedBoundary` reaches a later operation, but the factory and its tests must establish that the type is constructed correctly.

### 5. Make invalid operations unavailable

Expose only operations that have domain meaning. For example:

```text
Distance + Distance -> Distance
Distance * Distance -> Area
Area * Distance -> Volume
Distance + Area -> unavailable
LayerId == DrawingId -> unavailable
```

Do not add broad implicit conversions or generic arithmetic merely to make call sites shorter. Convenience must not remove the constraint being tested.

### 6. Convert at system boundaries

Primitive and host-specific values are allowed at serialization, UI, configuration, and AutoCAD API boundaries. Convert them to domain types immediately after reading them, and convert back only when calling the external boundary.

Keep parsing, handle lookup, unit conversion, and coordinate transformation behind named adapters or extensions. Core domain operations should not need to remember host conventions.

### 7. Introduce a type only when it rejects a real mistake

Every new domain type must answer both questions:

1. Which invalid values or operations does it prevent?
2. At which boundary can that mistake currently enter the system?

If a wrapper rejects no plausible mistake, do not add it. Strong typing is not permission to create speculative abstractions, generic frameworks, or one-property wrappers throughout the solution.

## Nullability and outcomes

Use `null` only when absence is a meaningful domain state. Otherwise use a required value, a named optional state, or an existing result type.

Expected failures should be visible in the return type. Exceptions remain appropriate for broken assumptions or unexpected host failures; they should not be used to model ordinary branching.

Do not invent a new result framework for this experiment. Reuse the project result type where it fits.

## Experiment procedure

1. Select one small workflow with a known risk, such as unit conversion, coordinate transformation, or mixing identifiers.
2. Record the current boundary types and two or three concrete invalid calls that still compile.
3. Introduce the minimum domain types needed to reject those calls.
4. Add compile-time or architecture checks where practical, plus focused runtime tests for validation and conversion factories.
5. Build the solution with zero warnings and run the relevant managed tests.
6. Perform separate AutoCAD verification when the slice crosses a native host boundary.
7. Compare the result with the baseline before extending the approach.

## Evaluation

Judge the experiment by evidence, not by the number of new types.

Useful questions are:

- Which previously valid but incorrect calls no longer compile?
- Which runtime checks moved to a single construction boundary?
- Can an agent discover the correct operation from the available types and methods?
- How much conversion or mapping code was added?
- Did the change make host integration or debugging harder?
- Did tests become smaller because illegal states cannot reach them?
- Are any important invariants still documented only in prose?

The experiment is successful only if the safety gained is concrete and the added ceremony remains local and understandable.

## Non-goals

- Replacing all `double`, `int`, `string`, or `Guid` values in the repository.
- Designing a reusable units or refinement-type framework before a real use case exists.
- Reproducing dependent types in C#.
- Treating a successful build as proof of AutoCAD behavior.
- Moving validation into types when the property can change after construction without being revalidated.
- Adding types solely to make the architecture appear more sophisticated.

## Starting candidates for CAD Lens

The first experiment should choose only one candidate:

- world coordinates versus viewport or screen coordinates;
- drawing distances versus UI pixel distances;
- radians versus degrees at a transformation boundary;
- AutoCAD object identity versus a stable domain identity;
- a validated non-empty highlight target set;
- an active lens session token versus a stale request token.

These are investigation targets, not approved design decisions. Confirm the actual boundary and failure mode in the current code before selecting one.

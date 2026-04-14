---
description: "Use when writing or editing C# files in this repository. Covers TwitchChatOverlay C# code style, naming, namespaces, using directives, modifiers, var usage, null handling, expression-bodied members, and formatter expectations for WPF and Prism code."
applyTo: "**/*.cs"
---

# TwitchChatOverlay C# Code Style

- Follow nearby code first. When there is no strong local precedent, apply the rules below.
- Keep generated code consistent with the repository's existing WPF and Prism patterns.

## File and Type Structure

- Use block-scoped namespaces, not file-scoped namespaces.
- Place using directives outside the namespace.
- Do not split System usings into a separate group unless the surrounding file already does so.
- Use explicit accessibility modifiers for non-interface members.
- Keep modifier order consistent with the repository convention:
  public, private, protected, internal, file, static, extern, new, virtual, abstract, sealed, override, readonly, unsafe, required, volatile, async.
- Prefer sealed classes when inheritance is not intended and the type is effectively closed.
- Prefer auto-properties when no custom accessor logic is needed.

## Naming

- Prefix interfaces with I.
- Use PascalCase for classes, structs, enums, interfaces, properties, events, and methods.
- Use _camelCase for private and internal instance fields.
- Use s_camelCase for static fields.
- Keep tuple element names and anonymous type member names inferred when they are already clear from context.

## Member Style

- Prefer this. when accessing instance fields, properties, methods, and events.
- Prefer expression-bodied members for simple properties, accessors, indexers, and small lambdas.
- Keep constructors, methods, operators, and local functions as block bodies unless there is a strong readability reason to do otherwise.
- Prefer simple property accessors and init-only properties where they fit the model.
- Prefer readonly fields when values are assigned only during construction.
- Prefer readonly structs and readonly struct members when the type is naturally immutable.

## Variables and Type Inference

- Prefer var for locals when the type is obvious from the right-hand side or when repeating the explicit type adds noise.
- Use an explicit type when it materially improves readability or prevents ambiguity.
- Prefer implicitly typed lambdas when the delegate shape is obvious.
- Prefer inline out variable declarations and deconstruction when they make the code clearer.

## Expressions and Control Flow

- Always use braces for control-flow statements, even for single-line bodies.
- Prefer null-propagation, null-coalescing, and throw expressions when they simplify the code.
- Prefer object initializers, collection initializers, and collection expressions when the target type is clear and the result is easier to scan.
- Prefer compound assignment, inferred tuple names, inferred anonymous type member names, and simplified interpolation when they improve clarity.
- Prefer simplified boolean expressions.
- Prefer is null style checks over reference equality helpers.
- Use conditional expressions only when they are clearly easier to read than an if block.
- Do not force switch expressions or pattern matching everywhere. Use property patterns, not patterns, and modern matching only when the result is clearer than straightforward procedural code.
- Prefer method group conversions when they do not make overload resolution or intent harder to understand.
- Prefer local functions over anonymous functions when the logic is named, reused, or easier to understand as a local helper.

## Resource and Lifetime Management

- Prefer explicit using statement blocks over collapsing disposal scope when the lifetime should stay visually obvious.
- Keep disposal and cleanup paths easy to trace, especially in service classes and media or IO related code.

## Formatting Expectations

- Use 4 spaces for indentation and keep CRLF line endings.
- Place opening braces on a new line for namespaces, types, members, and control-flow blocks.
- Keep else, catch, and finally on their own lines.
- Let the formatter handle low-level spacing, wrapping, and blank-line details unless the surrounding file intentionally does something different.
- Preserve compact single-line statements or blocks only when they remain easy to read and already match the local style.

## Repository-Specific Guidance

- Favor straightforward, debuggable code over stylistic cleverness.
- In DI registration, WPF startup, and service orchestration code, optimize for explicit intent and maintainability.
- When a new rule conflicts with the surrounding file, preserve local consistency unless the change is explicitly meant to refactor that area.
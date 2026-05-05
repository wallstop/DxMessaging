## Description

<!-- Provide a clear and concise description of your changes -->

## Related Issue

<!-- Link to the issue this PR addresses -->

Fixes #

## Type of Change

<!-- Check all that apply -->

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)
- [ ] Documentation update
- [ ] Refactor (code change that neither fixes a bug nor adds a feature)

## Checklist

<!-- Ensure all items are completed before requesting review -->

- [ ] All tests pass locally
- [ ] Code is properly formatted
- [ ] I have added tests that prove my fix is effective or my feature works
- [ ] I have updated the documentation accordingly
- [ ] I have updated the [CHANGELOG](../CHANGELOG.md)
- [ ] My changes do not introduce breaking changes, or breaking changes are documented

### Performance numbers

<!--
REQUIRED if this PR modifies hot-path files:
  Runtime/Core/MessageBus/MessageBus.cs
  Runtime/Core/MessageHandler.cs
  Runtime/Core/Pooling/**

The perf-numbers-check workflow gates on this section's presence.

Format:
  Scenario | Baseline (commit 25a4dcc) | This PR | Delta
  --- | --- | --- | ---
  UntargetedFlood_OneHandler (Mono Editor) | X.XX M emits/sec | Y.YY M emits/sec | +Z.Z%
  ...

Acceptable substitutions:
  - "N/A - refactor only <one-line justification>" when the change cannot
    affect runtime perf, e.g. comments / docs / tests-only changes.
  - "N/A - non-hot-path edit only <one-line description>" to describe what was
    actually touched, e.g. settings asset shape or editor tooling.

See .llm/skills/performance/dispatch-hot-path.md for the budget and the
T0 benchmark harness invocation.
-->

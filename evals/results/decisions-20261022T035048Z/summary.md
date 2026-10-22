# Decision run 20261022T035048Z-afd4de

- models: claude-haiku-4-5
- cases: 8 (decided 8)
- decision accuracy: 7/8 (0.875)
- cause accuracy (expected escalate): 4/4 (1.000)
- model decision accuracy: 7/8
- citations valid: 8/8
- guard: 0 overrides in 8 checked
- steps: total 16, median 2
- tool calls: 32 model, 8 guard
- cost: US$0.00000000 incurred, US$0.06208400 notional (0 unpriced)
- cache: 16/16 answered steps from the cache
- tokens in/out: 48304/2756
- grader: decision-grader-001, labels decision-labels-1

| case | expected | decision | correct | cause ok | citations ok | guard | steps | tool calls | incurred |
|---|---|---|---|---|---|---|---|---|---|
| dc-001 | approve | approve | true | - | true | pass | 2 | 4 | 0.00000000 |
| dc-002 | reject | reject | true | - | true | pass | 2 | 4 | 0.00000000 |
| dc-003 | escalate/policy_warning | escalate/policy_warning | true | true | true | pass | 2 | 4 | 0.00000000 |
| dc-004 | reject | approve | false | - | true | pass | 2 | 4 | 0.00000000 |
| dc-005 | escalate/extraction_not_success | escalate/extraction_not_success | true | true | true | pass | 2 | 4 | 0.00000000 |
| dc-006 | escalate/over_limit | escalate/over_limit | true | true | true | pass | 2 | 4 | 0.00000000 |
| dc-007 | approve | approve | true | - | true | pass | 2 | 4 | 0.00000000 |
| dc-008 | escalate/extraction_not_success | escalate/extraction_not_success | true | true | true | pass | 2 | 4 | 0.00000000 |

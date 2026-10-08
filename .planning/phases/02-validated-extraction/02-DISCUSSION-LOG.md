# Phase 2: Validated Extraction - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-08
**Phase:** 2-Validated Extraction
**Areas discussed:** Target breadth, Regimes & tax rules, Repair policy, Fixtures & live proof

---

## Target breadth

| Option | Description | Selected |
|--------|-------------|----------|
| Full row | code, description, ncm, cst_csosn, cfop, unit, quantity, unit_price, total, ICMS base/rate/amount, IPI rate/amount | ✓ |
| Commercial core | description, ncm, cfop, unit, quantity, unit_price, total | |
| Core + cst_csosn | commercial core plus the CST/CSOSN code | |

| Option | Description | Selected |
|--------|-------------|----------|
| All required totals | 11 totals, all required (the DANFE prints 0,00) | ✓ |
| Core required, rest optional | spends ~9 union slots | |
| Minimal | products, discount, freight, IPI, invoice total | |

| Option | Description | Selected |
|--------|-------------|----------|
| IE + UF, CNPJ-or-CPF | IE nullable, UF; recipient tax_id + tax_id_kind; CPF check digits | ✓ |
| IE + UF, CNPJ only | CPF deferred | |
| Full address | IE, street, number, district, city, UF, CEP | |

| Option | Description | Selected |
|--------|-------------|----------|
| Operation nature + duplicatas | with DUP_SUM rule | ✓ |
| None | header, parties, items, totals only | |
| Add transport too | carrier, freight mode, volumes | |

**User's choice:** recommended option in each case.

---

## Regimes & tax rules

| Option | Description | Selected |
|--------|-------------|----------|
| Simples + Normal core | CSOSN 101..900 + CST 00/20/40/41/50/60/90; ST/deferral as warnings | ✓ |
| Full CST incl. ST | ST arithmetic and deferral too | |
| Simples only | match current skeleton data | |

| Option | Description | Selected |
|--------|-------------|----------|
| Inferred, not extracted | from cst_csosn code length | ✓ |
| Extracted field | tax_regime read from the Simples note | |

| Option | Description | Selected |
|--------|-------------|----------|
| R$0.01 per op, scaled sums | ±0.01 × n capped at R$1.00 for sums | ✓ |
| Flat R$0.01 | | |
| Exact for sums | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Datagen + grader primitives | pytest on ID generators and rounding | ✓ |
| Python validator twin | full port | |
| Vectors only for .NET + rounding | | |

**User's choice:** recommended option in each case.

---

## Repair policy

| Option | Description | Selected |
|--------|-------------|----------|
| Errors only | two severities; warnings never repaired | ✓ |
| Errors + warnings | | |
| Hard errors only | identity errors only | |

| Option | Description | Selected |
|--------|-------------|----------|
| Typed failure + last candidate | validation_failed with candidate and findings | ✓ |
| Bare typed failure | | |
| Success with errors attached | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Continue conversation | prior output + errors turn, cache_control on PDF | ✓ |
| Fresh request with errors | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Injected clock, fixed in evals | reference date from request / manifest as-of | ✓ |
| Wall clock everywhere | | |
| No 'now' rule | | |

**User's choice:** recommended option in each case.

---

## Fixtures & live proof

| Option | Description | Selected |
|--------|-------------|----------|
| Rework the 3 cases | Simples 101, Normal + IPI + installments, multi-page alphanumeric/CPF | ✓ |
| Add 1–2 cases, keep the 3 | | |
| No data change | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, xUnit over skeleton | ground-truth XML → Invoice → zero validator errors | ✓ |
| Defer to Phase 4 | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Header + totals + counts | new scalars, item/installment counts, validator outcome | ✓ |
| Positional item grading too | | |
| Unchanged | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, Haiku 4.5, US$1/run, US$5 phase | plus one max_repairs=0 run | ✓ |
| One run only | | |
| No live run | | |

**User's choice:** recommended option in each case.

---

## Claude's Discretion

- Rule-ID catalogue beyond the examples, exact field names, validator project placement, attempt/response shape, prompt wording, vector file location.

## Deferred Ideas

- ST/deferral arithmetic rules; transport and infCpl fields; barcode-first access key at intake.

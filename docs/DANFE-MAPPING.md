# DANFE to Invoice mapping

This is the documented mapping from the NF-e XML, through what the DANFE prints, to the
`Invoice` extraction target (DOM-02, D-05, D-22). It is implemented once in .NET (the
ground-truth mapper) and followed by the Python reader in `carimbo_evals.ground_truth`, so the
two stacks cannot drift without a test noticing.

"DANFE-visible" means what the synthetic DANFEs print. They are rendered by BrazilFiscalReport
1.2.0 from the generated XML, and the mapping reproduces that layout, quirks included.
DANFEs from other emitters may print other columns; layout variants are out of scope here.
The XML namespace is `http://www.portalfiscal.inf.br/nfe`. Wire decimals are strings: `Money`
has two signed decimals, `Decimal4` four unsigned decimals, `Rate` two unsigned decimals.

## Field mapping

| Invoice field | DANFE label (block) | XML source | Rule |
|---------------|---------------------|------------|------|
| `access_key` | CHAVE DE ACESSO, printed in groups of 4 | `infNFe/@Id` without the `NFe` prefix | Strip spaces, uppercase |
| `number` | Nº (header) | `ide/nNF` | Integer; the DANFE prints `000.346.154` |
| `series` | SÉRIE (header) | `ide/serie` | Integer |
| `issue_date` | DATA DA EMISSÃO, `dd/mm/yyyy` | First 10 characters of `ide/dhEmi` | ISO `YYYY-MM-DD`, the printed local date, never converted to UTC |
| `operation_nature` | NATUREZA DA OPERAÇÃO | `ide/natOp` | Verbatim |
| `issuer.cnpj` | CNPJ / CPF (emitter box) | `emit/CNPJ` | Unmasked, no dots, slash or dash |
| `issuer.name` | Emitter name (emitter box) | `emit/xNome` | Verbatim |
| `issuer.ie` | INSCRIÇÃO ESTADUAL (emitter box) | `emit/IE` | As printed (the generator prints `ISENTO` today); null when blank |
| `issuer.uf` | Address line `xMun - UF` (emitter box) | `emit/enderEmit/UF` | Two uppercase letters |
| `recipient.tax_id` | CNPJ / CPF with mask, for example `529.982.247-25` (DESTINATÁRIO) | `dest/CNPJ` or `dest/CPF` | Unmasked |
| `recipient.tax_id_kind` | Derived from the same box | Which of `dest/CNPJ` and `dest/CPF` exists | `cnpj` or `cpf`; 11 digits is a CPF |
| `recipient.name` | NOME / RAZÃO SOCIAL (DESTINATÁRIO) | `dest/xNome` | Verbatim |
| `recipient.ie` | INSCRIÇÃO ESTADUAL (DESTINATÁRIO) | `dest/IE` | Verbatim; a blank cell (for example `indIEDest` 9) becomes null |
| `recipient.uf` | UF (DESTINATÁRIO) | `dest/enderDest/UF` | Two uppercase letters |
| `items[].code` | CÓDIGO (items table) | `prod/cProd` | Verbatim |
| `items[].description` | DESCRIÇÃO (items table) | `prod/xProd` | Verbatim |
| `items[].ncm` | NCM/SH (items table) | `prod/NCM` | Verbatim, 8 digits |
| `items[].cst_csosn` | CST column, 3 or 4 digits (`000`, `040`, `020`, `0102`) | `orig` of the single child of `imposto/ICMS`, then `CSOSN` when `emit/CRT` is 1 or 4, else `CST` | Origin digit first. BrazilFiscalReport picks CSOSN for CRT 1 and 4, CST otherwise |
| `items[].cfop` | CFOP (items table) | `prod/CFOP` | Verbatim, 4 digits |
| `items[].unit` | UN. (items table) | `prod/uCom` | Verbatim |
| `items[].quantity` | QTD., printed with 4 decimals (`198,8210`) | `prod/qCom` | Decimal string with exactly 4 decimals, half-up |
| `items[].unit_price` | V.UNIT., printed with 4 decimals (`558,2882`) | `prod/vUnCom` | Decimal string with exactly 4 decimals, half-up |
| `items[].total` | V.TOTAL (items table) | `prod/vProd` | 2 decimals |
| `items[].icms_base` | BC.ICMS (items table) | `vBC` of the ICMS child | 2 decimals; an absent tag prints `0,00` and maps to `0.00` |
| `items[].icms_rate` | %ICMS (items table) | `pICMS` of the ICMS child | 2 decimals, unsigned; absent is `0.00` |
| `items[].icms_amount` | V.ICMS (items table) | `vICMS` of the ICMS child | 2 decimals; absent is `0.00` |
| `items[].ipi_rate` | %IPI (items table) | `imposto/IPI/IPITrib/pIPI` | 2 decimals, unsigned; absent is `0.00` |
| `items[].ipi_amount` | V.IPI (items table) | `imposto/IPI/IPITrib/vIPI` | 2 decimals; absent is `0.00`. The DANFE has no IPI base column |
| `totals.icms_base` | BASE DE CÁLCULO DO ICMS (CÁLCULO DO IMPOSTO) | `total/ICMSTot/vBC` | 2 decimals; absent is `0.00` |
| `totals.icms_amount` | VALOR DO ICMS | `total/ICMSTot/vICMS` | 2 decimals; absent is `0.00` |
| `totals.icms_st_base` | BASE DE CÁLC. ICMS S.T. | `total/ICMSTot/vBCST` | 2 decimals; absent is `0.00` |
| `totals.icms_st_amount` | VALOR DO ICMS SUBST. | `total/ICMSTot/vST` | 2 decimals; absent is `0.00` |
| `totals.products_total` | VALOR TOTAL DOS PRODUTOS | `total/ICMSTot/vProd` | 2 decimals; absent is `0.00` |
| `totals.freight` | VALOR DO FRETE | `total/ICMSTot/vFrete` | 2 decimals; absent is `0.00` |
| `totals.insurance` | VALOR DO SEGURO | `total/ICMSTot/vSeg` | 2 decimals; absent is `0.00` |
| `totals.discount` | DESCONTO | `total/ICMSTot/vDesc` | 2 decimals; absent is `0.00` |
| `totals.other_expenses` | OUTRAS DESPESAS ACESSÓRIAS | `total/ICMSTot/vOutro` | 2 decimals; absent is `0.00` |
| `totals.ipi_amount` | VALOR TOTAL DO IPI | `total/ICMSTot/vIPI` | 2 decimals; absent is `0.00` |
| `totals.invoice_total` | VALOR TOTAL DA NOTA | `total/ICMSTot/vNF` | 2 decimals. The only place the invoice total lives (D-22) |
| `installments[].number` | Duplicata number in the FATURA / DUPLICATAS text, `001  01/11/2026  5,00` | `cobr/dup/nDup` | Verbatim |
| `installments[].due_date` | Due date in the same text | `cobr/dup/dVenc` | Printed `dd/mm/yyyy` to ISO `YYYY-MM-DD` |
| `installments[].amount` | Amount in the same text | `cobr/dup/vDup` | 2 decimals, unformatted |

Items come one per `det` in document order, installments one per `cobr/dup` in order; an
invoice with no `cobr` has an empty `installments` list. A tax column printed `0,00` is `"0.00"`.
When the model reads a DANFE, pt-BR numbers use `.` as the thousands separator and `,` as the
decimal separator (`183.737,44` is `183737.44`), and masks are removed from CNPJ, CPF and key.

## Not on the DANFE

These XML values are not printed, so they are not in the target and no field is derived from them:

- `emit/CRT` (the regime code). It is never printed; the regime is inferred from the length of `cst_csosn` (3 digits is Normal, 4 digits is Simples).
- Per-item PIS and COFINS.
- Per-item discount (BrazilFiscalReport has no discount column).
- `ide/idDest` and `ide/finNFe`.
- The `fat` group of the billing block (`nFat`, `vOrig`, `vDesc`, `vLiq`). It is printed, but only the installments are in the target.
- VALOR APROX. TRIBUTOS, the twelfth box of the tax calculation block.

## Limitations

- **vNF is a subset.** The SEFAZ rule behind rejection 610 sums `vProd - vDesc - vICMSDeson + vST + vFCPST + vFrete + vSeg + vOutro + vII + vIPI + vIPIDevol + vServ`. The DANFE-visible check uses `products_total - discount + icms_st_amount + freight + insurance + other_expenses + ipi_amount`, and assumes `vICMSDeson`, `vFCPST`, `vII`, `vIPIDevol` and `vServ` are zero because the target has no box for them.
- **CST 60 is treated as not taxed.** The item ICMS amount must be `0.00` for it, as for the other codes in that family.
- **The IPI base equals the item total.** The DANFE has no IPI base column, so `ipi_amount` is checked against `items[].total` times `ipi_rate`.
- **IE is kept as printed.** `ISENTO` stays `ISENTO`, and a blank cell is null.
- **Dates are the printed local date.** There is no time zone conversion.
- **Quantities and unit prices keep four decimals**, matching the DANFE; they are not rounded to cents.
- **Layout is the synthetic one.** Other emitters' DANFE layouts are not modelled in this phase.

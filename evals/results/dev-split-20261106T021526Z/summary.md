# Run 20261106T021527Z-a8a10f

- INCOMPLETE: 3 infrastructure_failure, 0 harness_error, 0 selected cases not run
- models: claude-haiku-4-5-20251001
- cases: 54 (success 16, validation_failed 35, infrastructure_failure 3)
- cost: US$0.00000000 incurred, US$2.30699480 notional (3 unpriced)
- cache: 125/125 answered attempts from the cache
- tokens in/out: 431589/277962
- model latency: p50 31869 ms, p95 125640 ms (original latency, also on a replay)
- validation_failed (caught): 35
- grader: grader-003, tolerance 0.00

## Correct invoices

| row | cases | invoices | excluded | correct | rate | 95% interval |
|---|---|---|---|---|---|---|
| dev text (dev) | 30 | 30 | 0 | 12 | 0.4000 | [0.2459, 0.5768] |
| dev image (dev) | 21 | 3 | 3 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev pooled (dev) | 51 | 30 | 3 | 12 | 0.2353 | [0.1193, 0.4114] |

- dev text: 12 of 30 cases carry a DANFE wrap hazard (see D-47)

## Rendering

| row | cases | invoices | excluded | correct | rate | 95% interval |
|---|---|---|---|---|---|---|
| dev rendering=text (dev) | 30 | 30 | 0 | 12 | 0.4000 | [0.2459, 0.5768] |
| dev rendering=image_clean (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=barcode_damaged (dev) | 2 | 2 | 1 | 0 | 0.0000 | [0.0000, 0.6576] |
| dev rendering=rotation/mild (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=rotation/severe (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=blur/mild (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=blur/severe (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=low_dpi/mild (dev) | 2 | 2 | 1 | 0 | 0.0000 | [0.0000, 0.6576] |
| dev rendering=low_dpi/severe (dev) | 2 | 2 | 1 | 0 | 0.0000 | [0.0000, 0.6576] |

## Items

| scope | true rows | predicted rows | matched | precision | recall | F1 | all values correct |
|---|---|---|---|---|---|---|---|
| overall | 828 | 862 | 691 | 0.8016 [0.6746, 0.9730] | 0.8345 [0.6837, 1.0000] | 0.8178 [0.6803, 0.9739] | 0.7597 [0.5445, 0.9984] |
| dev (dev) | 828 | 862 | 691 | 0.8016 [0.6746, 0.9730] | 0.8345 [0.6837, 1.0000] | 0.8178 [0.6803, 0.9739] | 0.7597 [0.5445, 0.9984] |

## Silent errors

| scope | success cases | invoices | silent | rate | 95% interval | payment | other |
|---|---|---|---|---|---|---|---|
| overall | 16 | 16 | 4 | 0.2500 | [0.1018, 0.4950] | 0 | 4 |
| dev (dev) | 16 | 16 | 4 | 0.2500 | [0.1018, 0.4950] | 0 | 4 |

## Failure tags

| tag | values | cases |
|---|---|---|
| trailing_rows_lost | 70 | 13 |
| dropped_row | 94 | 13 |
| invented_row | 172 | 21 |
| value_from_other_row | 5 | 2 |
| value_from_other_box | 1 | 1 |
| zero_for_blank | 10 | 4 |
| digit_transposition | 4 | 3 |
| digit_substitution | 79 | 16 |
| digit_count | 5 | 4 |
| text_near_miss | 20 | 14 |
| text_wrong | 62 | 23 |
| tax_code_wrong | 21 | 12 |
| date_wrong | 19 | 15 |
| untagged | 265 | 35 |

827 wrong values, 265 untagged, tag list failure-tags-1

## Cases

| case_id | status | fields | attempts | hits | findings | total delta | cost | latency ms | trace id |
|---|---|---|---|---|---|---|---|---|---|
| nfe-0001 | validation_failed | 26/27 | 3 | 3/3 | 2E/1W | 0.00 | 0.02396390 | 52 | a6d3feb21d5fc3498c9aca017b1169e0 |
| nfe-0002 | success | 26/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.01060300 | 46 | 5513f0b84a933cf42baa45a3c4ab32df |
| nfe-0004 | success | 27/27 | 2 | 2/2 | 0E/3W | 0.00 | 0.02419150 | 9 | de5217a93c58f6b9bcca4d2744307f6d |
| nfe-0007 | validation_failed | 25/27 | 3 | 3/3 | 1E/0W | 0.00 | 0.02331490 | 9 | db3ee756492704e32b83558ce803f641 |
| nfe-0011 | validation_failed | 26/27 | 3 | 3/3 | 2E/0W | 0.00 | 0.03479395 | 8 | 3591b89db923cf3ff871451824dc8cea |
| nfe-0017 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.01618925 | 5 | 02dd4b93e4ea044601c996cc767075f0 |
| nfe-0023 | success | 27/27 | 1 | 1/1 | 0E/8W | 0.00 | 0.02643800 | 7 | cde6414c16f058de1abce413322e7dbe |
| nfe-0024 | success | 27/27 | 2 | 2/2 | 0E/0W | 0.00 | 0.02827850 | 9 | 379605d37b6327f6f7ece61c4a94e483 |
| nfe-0026 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.02438800 | 5 | 0d6f3b633f6637bba7066474a302ed9e |
| nfe-0027 | validation_failed | 25/27 | 3 | 3/3 | 5E/0W | 0.00 | 0.06949625 | 25 | 40b8314a6583f8cf580410dcdf987416 |
| nfe-0029 | validation_failed | 25/27 | 3 | 3/3 | 2E/33W | 0.00 | 0.09337280 | 32 | cbfeea3cc1a7d55e4221c69c74485c37 |
| nfe-0033 | validation_failed | 25/27 | 3 | 3/3 | 6E/0W | 0.00 | 0.10446425 | 27 | 7fe24933110aff3abf568d0dd2138cbe |
| nfe-0037 | validation_failed | 26/27 | 3 | 3/3 | 5E/0W | 0.00 | 0.13061040 | 34 | ad9a4e28564956c9b073e05f79297510 |
| nfe-0038 | validation_failed | 22/27 | 3 | 3/3 | 1E/10W | 295761.29 | 0.27736020 | 63 | 1dc0d44e5e346965af47540aa3c35612 |
| nfe-0042 | success | 27/27 | 2 | 2/2 | 0E/0W | 0.00 | 0.06276070 | 12 | 483892f24aeeb7e84ead037fdfb461a7 |
| nfe-0047 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.01063050 | 1 | 3ceabb84ad2efbdaaefa95f3a9443ba1 |
| nfe-0048 | validation_failed | 26/27 | 3 | 3/3 | 2E/0W | 0.00 | 0.03392680 | 7 | 59f3b937bfdd3e89387f42ae6489baa0 |
| nfe-0053 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.01410175 | 1 | 7d7ab1eebabe597ce4f4f6980a1ab613 |
| nfe-0057 | validation_failed | 26/27 | 3 | 3/3 | 3E/0W | 0.00 | 0.02855270 | 5 | be2f6912918bfcab8d1ec88770fb5c05 |
| nfe-0059 | success | 27/27 | 2 | 2/2 | 0E/0W | 0.00 | 0.02441400 | 4 | b8473243ce7e27fa891b0c2ea8d4c3b0 |
| nfe-0060 | validation_failed | 25/27 | 3 | 3/3 | 6E/0W | 0.00 | 0.07112525 | 16 | 4b1ebdc68b39d17b99cd695a97d33f34 |
| nfe-0061 | validation_failed | 26/27 | 3 | 3/3 | 1E/0W | 0.00 | 0.05627090 | 12 | 71049473ebcc60286d3beace68b6b80d |
| nfe-0064 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.02581175 | 4 | 47a323fe0d1356ce1974873d82c9c8b9 |
| nfe-0067 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.02335925 | 4 | f19c391e9460fc6b8bae30393aea34f3 |
| nfe-0071 | validation_failed | 26/27 | 3 | 3/3 | 1E/0W | 0.00 | 0.04586595 | 8 | 9369e527dd4df625d9b1d6f1d56f4e73 |
| nfe-0077 | validation_failed | 25/27 | 3 | 3/3 | 6E/0W | 0.00 | 0.10220550 | 22 | 1c0d36dc8189cdc88ccf04b9c2bef085 |
| nfe-0079 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.03571800 | 7 | 77d895dc8911b158c6769c84f4c59287 |
| nfe-0084 | success | 26/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.03601800 | 5 | cf617003da281fb6e79fbd448fdd8769 |
| nfe-0085 | success | 27/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.03677925 | 5 | c426b17e6d4206c416e0b36954338a66 |
| nfe-0088 | success | 26/27 | 1 | 1/1 | 0E/0W | 0.00 | 0.03722050 | 5 | 6c70495d2b3100c34121abd4fdff5751 |
| nfe-0091 | validation_failed | 18/27 | 3 | 3/3 | 11E/0W | -216.27 | 0.02355110 | 6 | 4a80dad777aeadb8a601fae53dc3d400 |
| nfe-0092 | validation_failed | 17/27 | 3 | 3/3 | 13E/0W | -4362.42 | 0.02388910 | 4 | cc5508acb5feec319725be5b754ed662 |
| nfe-0093 | validation_failed | 16/27 | 3 | 3/3 | 11E/1W | -27856.08 | 0.02158310 | 4 | 5d043a954bd1df7ec8976afe4bfee377 |
| nfe-0094 | validation_failed | 15/27 | 3 | 3/3 | 10E/0W | 66316.81 | 0.02359810 | 4 | ac788965f97e83faafad672809ecf794 |
| nfe-0095 | validation_failed | 11/27 | 3 | 3/3 | 11E/0W | -18805.47 | 0.02335910 | 11 | 3725c27adb984fc03bbf4ce6f2ec1fa3 |
| nfe-0096 | validation_failed | 15/27 | 3 | 3/3 | 10E/0W | 3563.46 | 0.02427810 | 5 | 035c756c40fbda31cba32a1101ca8891 |
| nfe-0097 | validation_failed | 12/27 | 3 | 3/3 | 8E/0W | -5806.67 | 0.02335010 | 4 | cf24d6136c74ba29bf77e7f37ff21f41 |
| nfe-0098 | validation_failed | 20/27 | 3 | 3/3 | 7E/0W | 0.00 | 0.02305010 | 5 | 144ce4684ab781cb5ae37c71736219be |
| nfe-0123 | validation_failed | 17/27 | 3 | 3/3 | 8E/0W | -14056.75 | 0.03233210 | 6 | f37ea49101a93e5dd3ac2ebe1dc7f6e8 |
| nfe-0124 | validation_failed | 19/27 | 3 | 3/3 | 8E/0W | -20069.70 | 0.03363110 | 9 | 2afd0e803c30effaa1af4eb2004dd639 |
| nfe-0125 | validation_failed | 17/27 | 3 | 3/3 | 7E/0W | -25346.20 | 0.03194610 | 7 | 073f2ee8a34aea74fce0e25638e3c46d |
| nfe-0126 | validation_failed | 18/27 | 3 | 3/3 | 14E/6W | -2669.64 | 0.03360210 | 8 | 67714051a4201784ed6bc60c100f688b |
| nfe-0127 | validation_failed | 16/27 | 3 | 3/3 | 8E/0W | -29716.97 | 0.02768210 | 6 | 6cdecf28c3326953bccc3217cc3e54c7 |
| nfe-0128 | validation_failed | 18/27 | 3 | 3/3 | 10E/7W | -5123.55 | 0.03175210 | 6 | 0781a7432edd7e27716e440e1e23fb68 |
| nfe-0129 | validation_failed | 11/27 | 3 | 3/3 | 12E/0W | -52150.79 | 0.02746110 | 6 | a8ff6f6e72edefcd8cbaf17fbb3765ad |
| nfe-0130 | validation_failed | 16/27 | 3 | 3/3 | 9E/0W | -12745.54 | 0.03351910 | 9 | 0aa96801b7774b3d709cc148bb4f9aa5 |
| nfe-0139 | validation_failed | 18/27 | 3 | 3/3 | 7E/0W | -50922.93 | 0.06647510 | 14 | 66ae379c9ba23fe48277638fb23d2aeb |
| nfe-0140 | validation_failed | 19/27 | 3 | 3/3 | 8E/0W | 9386.63 | 0.06851610 | 18 | 08846bd072f786c4ae505789c8e940c9 |
| nfe-0141 | validation_failed | 18/27 | 3 | 3/3 | 22E/0W | -56489.87 | 0.06739310 | 15 | 3eea99091d12889c7f261de20213b306 |
| nfe-0142 | validation_failed | 16/27 | 3 | 3/3 | 9E/0W | -39511.46 | 0.06658310 | 16 | 86490fb819c225b77c458598f35d419e |
| nfe-0143 | validation_failed | 15/27 | 3 | 3/3 | 29E/0W | 4841.94 | 0.06721710 | 16 | 0e04a80a691aafc830de20cff2553a42 |
| nfe-0144 | infrastructure_failure | - | 1 | 0/0 | 0E/0W | - | - | 1 | 9f61b7f57a0ec6b51f2ce188102f36d7 |
| nfe-0145 | infrastructure_failure | - | 1 | 0/0 | 0E/0W | - | - | 0 | a22ce681da1d3231120e640ff07481c5 |
| nfe-0146 | infrastructure_failure | - | 1 | 0/0 | 0E/0W | - | - | 0 | 5bbae7ce9bee493f65c1ee5420799e35 |

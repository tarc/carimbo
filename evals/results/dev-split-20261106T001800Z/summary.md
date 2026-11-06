# Run 20261106T001800Z-9e906a

- INCOMPLETE: 0 infrastructure_failure, 0 harness_error, 3 selected cases not run, stopped: cost_cap
- models: claude-haiku-4-5-20251001
- cases: 51 (success 16, validation_failed 35)
- cost: US$2.30699480 incurred, US$2.30699480 notional (0 unpriced)
- cache: 0/125 answered attempts from the cache
- tokens in/out: 431589/277962
- model latency: p50 31869 ms, p95 125640 ms (original latency, also on a replay)
- validation_failed (caught): 35
- grader: grader-003, tolerance 0.00

## Correct invoices

| row | cases | invoices | excluded | correct | rate | 95% interval |
|---|---|---|---|---|---|---|
| dev text (dev) | 30 | 30 | 0 | 12 | 0.4000 | [0.2459, 0.5768] |
| dev image (dev) | 21 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev pooled (dev) | 51 | 30 | 0 | 12 | 0.2353 | [0.1193, 0.4114] |

- dev text: 12 of 30 cases carry a DANFE wrap hazard (see D-47)

## Rendering

| row | cases | invoices | excluded | correct | rate | 95% interval |
|---|---|---|---|---|---|---|
| dev rendering=text (dev) | 30 | 30 | 0 | 12 | 0.4000 | [0.2459, 0.5768] |
| dev rendering=image_clean (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=barcode_damaged (dev) | 2 | 2 | 0 | 0 | 0.0000 | [0.0000, 0.6576] |
| dev rendering=rotation/mild (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=rotation/severe (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=blur/mild (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=blur/severe (dev) | 3 | 3 | 0 | 0 | 0.0000 | [0.0000, 0.5615] |
| dev rendering=low_dpi/mild (dev) | 2 | 2 | 0 | 0 | 0.0000 | [0.0000, 0.6576] |
| dev rendering=low_dpi/severe (dev) | 2 | 2 | 0 | 0 | 0.0000 | [0.0000, 0.6576] |

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
| nfe-0001 | validation_failed | 26/27 | 3 | 0/3 | 2E/1W | 0.00 | 0.02396390 | 22915 | 953a4130a5751c2b9ef459a4775c8bfc |
| nfe-0002 | success | 26/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.01060300 | 9305 | f1ddbb39aaa5a24975cebb5e22d0fc70 |
| nfe-0004 | success | 27/27 | 2 | 0/2 | 0E/3W | 0.00 | 0.02419150 | 22633 | 4b04d7aa145117b5bc6ee41beae14454 |
| nfe-0007 | validation_failed | 25/27 | 3 | 0/3 | 1E/0W | 0.00 | 0.02331490 | 18113 | 91a49a267486c1a9e4a575a620f0b09d |
| nfe-0011 | validation_failed | 26/27 | 3 | 0/3 | 2E/0W | 0.00 | 0.03479395 | 34382 | 3464eb388655266484c44d6b548d4bb4 |
| nfe-0017 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.01618925 | 12740 | 5b0eba207e0f675765f94e3142d70ef4 |
| nfe-0023 | success | 27/27 | 1 | 0/1 | 0E/8W | 0.00 | 0.02643800 | 28944 | bc6ddd49381956ec68689740da1b5ca6 |
| nfe-0024 | success | 27/27 | 2 | 0/2 | 0E/0W | 0.00 | 0.02827850 | 28489 | d04a956713ba1fc8b08146492bf49cdf |
| nfe-0026 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.02438800 | 25540 | 94526b6e11e607a25cec830437096dd8 |
| nfe-0027 | validation_failed | 25/27 | 3 | 0/3 | 5E/0W | 0.00 | 0.06949625 | 77184 | 659f0c245e587a6a79575e6d4c9ce37e |
| nfe-0029 | validation_failed | 25/27 | 3 | 0/3 | 2E/33W | 0.00 | 0.09337280 | 116098 | 6abe21c9cc0de8f47186235f451c4640 |
| nfe-0033 | validation_failed | 25/27 | 3 | 0/3 | 6E/0W | 0.00 | 0.10446425 | 125670 | fbce5c58c598574b8302036c081feca7 |
| nfe-0037 | validation_failed | 26/27 | 3 | 0/3 | 5E/0W | 0.00 | 0.13061040 | 157170 | 01ddb1e97592d6bfba0fbd3b78f2a462 |
| nfe-0038 | validation_failed | 22/27 | 3 | 0/3 | 1E/10W | 295761.29 | 0.27736020 | 250436 | 036deed23a3eb0453119d14a9c46249c |
| nfe-0042 | success | 27/27 | 2 | 0/2 | 0E/0W | 0.00 | 0.06276070 | 77131 | e78f436341f67c231236aa3c54bd812e |
| nfe-0047 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.01063050 | 5143 | 9f6ada5fd6de89dfe5e5556b5c84d368 |
| nfe-0048 | validation_failed | 26/27 | 3 | 0/3 | 2E/0W | 0.00 | 0.03392680 | 32063 | b27045c88d6217b812f75801bd54e9cf |
| nfe-0053 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.01410175 | 10790 | 2e494c6c47569eea5c8d5ac230840d85 |
| nfe-0057 | validation_failed | 26/27 | 3 | 0/3 | 3E/0W | 0.00 | 0.02855270 | 24667 | 05d33409332f8b29aff5a40d345e324e |
| nfe-0059 | success | 27/27 | 2 | 0/2 | 0E/0W | 0.00 | 0.02441400 | 22799 | 7285ef4c909bef87a6a0e61ca642783a |
| nfe-0060 | validation_failed | 25/27 | 3 | 0/3 | 6E/0W | 0.00 | 0.07112525 | 82212 | 9bcee231dd6f1619a1dc10d101b753b4 |
| nfe-0061 | validation_failed | 26/27 | 3 | 0/3 | 1E/0W | 0.00 | 0.05627090 | 61101 | 6a42750a9c05230c1e4e8e6a5dbb4932 |
| nfe-0064 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.02581175 | 26523 | 54ee74d1a7d884b88fe12c14529e4532 |
| nfe-0067 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.02335925 | 23798 | 8fb5a06d703ed04d0105a012e747284d |
| nfe-0071 | validation_failed | 26/27 | 3 | 0/3 | 1E/0W | 0.00 | 0.04586595 | 47910 | aa442ff63e97af11a44144b5ac9fdaf6 |
| nfe-0077 | validation_failed | 25/27 | 3 | 0/3 | 6E/0W | 0.00 | 0.10220550 | 120137 | ebc5968979c8b341ba12563752a40f90 |
| nfe-0079 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.03571800 | 39783 | 4af937326bc7a663be64ddef99e94e97 |
| nfe-0084 | success | 26/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.03601800 | 38659 | faec99484e4164b8344e268ce7f9e226 |
| nfe-0085 | success | 27/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.03677925 | 40039 | d9486ff7898dcd15830d3c88eb1fe960 |
| nfe-0088 | success | 26/27 | 1 | 0/1 | 0E/0W | 0.00 | 0.03722050 | 41577 | b1e7d4031b8997498d92adabc7eb4776 |
| nfe-0091 | validation_failed | 18/27 | 3 | 0/3 | 11E/0W | -216.27 | 0.02355110 | 22168 | 1cfe050d7cc1d55d59b68c27f142bb01 |
| nfe-0092 | validation_failed | 17/27 | 3 | 0/3 | 13E/0W | -4362.42 | 0.02388910 | 22685 | 8b524e2dcddb5f6831a5d79f08fd3e18 |
| nfe-0093 | validation_failed | 16/27 | 3 | 0/3 | 11E/1W | -27856.08 | 0.02158310 | 18590 | 87568a1826328c32cbba962e44896564 |
| nfe-0094 | validation_failed | 15/27 | 3 | 0/3 | 10E/0W | 66316.81 | 0.02359810 | 21274 | 7aea1bffee00ebf0d6550ab5a025bd07 |
| nfe-0095 | validation_failed | 11/27 | 3 | 0/3 | 11E/0W | -18805.47 | 0.02335910 | 20305 | e4c837bff774fd5a9e06560b228b23e6 |
| nfe-0096 | validation_failed | 15/27 | 3 | 0/3 | 10E/0W | 3563.46 | 0.02427810 | 23324 | 03fe139845e83cebb13d1f934a40f6dd |
| nfe-0097 | validation_failed | 12/27 | 3 | 0/3 | 8E/0W | -5806.67 | 0.02335010 | 19696 | 802bf8caba404b75d3c32efeb0c70572 |
| nfe-0098 | validation_failed | 20/27 | 3 | 0/3 | 7E/0W | 0.00 | 0.02305010 | 21015 | 34be37c559efc9bc7317f1c8f2de64be |
| nfe-0123 | validation_failed | 17/27 | 3 | 0/3 | 8E/0W | -14056.75 | 0.03233210 | 32897 | 0cb9c785fdb8a18b5adc4981282cab16 |
| nfe-0124 | validation_failed | 19/27 | 3 | 0/3 | 8E/0W | -20069.70 | 0.03363110 | 34823 | e489247722204a32ec01d518db4e78d1 |
| nfe-0125 | validation_failed | 17/27 | 3 | 0/3 | 7E/0W | -25346.20 | 0.03194610 | 31919 | 98c7c727a40c25242c8346762e03cc0b |
| nfe-0126 | validation_failed | 18/27 | 3 | 0/3 | 14E/6W | -2669.64 | 0.03360210 | 34575 | 4fb4b0d8a6ebe8a02581584db6a1709c |
| nfe-0127 | validation_failed | 16/27 | 3 | 0/3 | 8E/0W | -29716.97 | 0.02768210 | 25766 | ca908ddd79fc13d16575023b51429f76 |
| nfe-0128 | validation_failed | 18/27 | 3 | 0/3 | 10E/7W | -5123.55 | 0.03175210 | 31110 | 3db2b8472d181a52a1df62a216bff2f1 |
| nfe-0129 | validation_failed | 11/27 | 3 | 0/3 | 12E/0W | -52150.79 | 0.02746110 | 25422 | 123350ba5a700ee48b697ef236374520 |
| nfe-0130 | validation_failed | 16/27 | 3 | 0/3 | 9E/0W | -12745.54 | 0.03351910 | 35742 | 9c163f1b1081148f823da2a2435414ea |
| nfe-0139 | validation_failed | 18/27 | 3 | 0/3 | 7E/0W | -50922.93 | 0.06647510 | 76657 | e035d0c88ed1978421e507d4eec1b24e |
| nfe-0140 | validation_failed | 19/27 | 3 | 0/3 | 8E/0W | 9386.63 | 0.06851610 | 80940 | 72018b8f4887f88e8a918a440e8f7029 |
| nfe-0141 | validation_failed | 18/27 | 3 | 0/3 | 22E/0W | -56489.87 | 0.06739310 | 79037 | 65c2e277562ca48f3996e1e315f51864 |
| nfe-0142 | validation_failed | 16/27 | 3 | 0/3 | 9E/0W | -39511.46 | 0.06658310 | 76170 | 6660e1f4970674be0fbfa4698c93b7d1 |
| nfe-0143 | validation_failed | 15/27 | 3 | 0/3 | 29E/0W | 4841.94 | 0.06721710 | 76578 | 40be6a450fff0e8eab21c7749fcec237 |

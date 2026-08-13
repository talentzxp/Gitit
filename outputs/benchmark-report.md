# GitIt v0.0.4 benchmark report

## Synthetic Ground Truth

- Family accuracy: 89.03%
- Parent precision / recall: 100.00% / 91.30%
- Branch / duplicate / abstention: 100.00% / 100.00% / 100.00%

## Template Siblings

- False family: 0.00%
- False edge: 0.00%

## External Real Corpus

- Infrastructure ready, manual corpus still required.
- Real Word: 0; WPS: 0; WeChat: 0

## Scaling Benchmark

| Files | Cold parse ms | Candidate pairs | Naive pairs | Reduction | Total ms |
|---:|---:|---:|---:|---:|---:|
| 10 | 12.0 | 90 | 90 | 0.00% | 40.4 |
| 50 | 46.8 | 500 | 2450 | 79.59% | 73.9 |
| 100 | 109.6 | 1000 | 9900 | 89.90% | 173.7 |
| 500 | 553.7 | 5000 | 249500 | 98.00% | 1052.6 |
| 1000 | 1032.3 | 10000 | 999000 | 99.00% | 2200.1 |
| 2000 | 1792.5 | 20000 | 3998000 | 99.50% | 5567.1 |

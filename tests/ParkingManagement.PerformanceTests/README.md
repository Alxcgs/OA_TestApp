# ParkingManagement Performance Tests (k6)

## Передумови
- API запущений локально (за замовчуванням `http://localhost:5000`).
- База даних PostgreSQL доступна.
- Під час старту API виконується `SeedData.Initialize(...)`, тому є великі тестові дані (10k+).

## Запуск
```bash
k6 run tests/ParkingManagement.PerformanceTests/load-entry-exit.k6.js
```

```bash
k6 run tests/ParkingManagement.PerformanceTests/stress-concurrent-near-full.k6.js
```

Якщо API слухає інший URL:
```bash
BASE_URL=http://localhost:8080 k6 run tests/ParkingManagement.PerformanceTests/load-entry-exit.k6.js
```

## Що перевіряється
- `load-entry-exit.k6.js`: стабільність потоку в'їзд → виїзд → оплата під високим трафіком.
- `stress-concurrent-near-full.k6.js`: конкурентні в'їзди в майже заповнену парковку.

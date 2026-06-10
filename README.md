# Slot Math Lab

No-code, node-based constructor for iGaming slot math.

## Local bootstrap

### One-command start

```bash
# Backend
cd backend && dotnet build && cd ..

# Frontend
cd frontend && npm ci && cd ..
```

### Backend

Requires [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```bash
cd backend
dotnet restore
dotnet build
dotnet format --verify-no-changes
dotnet test
```

### Frontend

Requires [Node.js 22](https://nodejs.org/).

```bash
cd frontend
npm ci
npm run build
npx tsc --noEmit
npm run lint
npm run dev        # start dev server
```

### Engine benchmarks

```bash
cd backend
dotnet run -c Release --project SlotMath.Benchmarks
```

Stopwatch-based throughput scenarios for both interpreters (Monte Carlo
loops, graph-compiled programs, reel draws, exact free-spin retrigger
memoisation).

### Project structure

```
slot-math-lab/
├── backend/
│   ├── SlotMath.Core/         # Pure math kernel
│   ├── SlotMath.Api/          # Web API host
│   ├── SlotMath.Core.Tests/   # xUnit tests
│   ├── SlotMath.Benchmarks/   # Engine throughput benchmarks
│   └── SlotMathLab.sln
├── frontend/                  # React 19 + TypeScript + Vite
├── docs/
│   └── PRD.md
└── .github/workflows/
    └── ci.yml
```

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

### Project structure

```
slot-math-lab/
├── backend/
│   ├── SlotMath.Core/         # Pure math kernel
│   ├── SlotMath.Api/          # Web API host
│   ├── SlotMath.Core.Tests/   # xUnit tests
│   └── SlotMathLab.sln
├── frontend/                  # React 19 + TypeScript + Vite
├── docs/
│   └── PRD.md
└── .github/workflows/
    └── ci.yml
```

# OrchDash

A fullscreen terminal dashboard for runs of the agent orchestrator: it reads `<repo>/.orchestrator/`
and shows run progress, tasks, running agents, the log and each session's conversation.

## Setup

Requires the .NET 10 SDK.

```powershell
dotnet restore AgentOrchestratorDashboard.slnx
```

## Test

```powershell
dotnet build AgentOrchestratorDashboard.slnx -warnaserror && dotnet test AgentOrchestratorDashboard.slnx --no-build
```

## Run

```powershell
dotnet run --project src/OrchDash -- <path to repo>
```

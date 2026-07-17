# CI/CD Pipeline Flow

Single-pipeline, two-stage design (`azure-pipelines.yml`): **Test** runs on every PR and on
every push to `main`; **Publish** runs only on `main`, and only if Test passed on that exact
commit. Deploys (`azure-pipelines-deploy-staging.yml`, `azure-pipelines-deploy-prod.yml`) are
always manual and always pull from the latest successful `main` run — never a PR-validation run.

## Why it's shaped this way

- `main` is branch-policy-gated: a PR cannot merge until Test is green.
- A PR's Test run only tests a **preview** of the merge (`refs/pull/N/merge`). If another PR
  lands on `main` in between validation and completion, the real merge commit could differ from
  what was tested — so Test runs again on the actual `main` commit before Publish is trusted to
  run. This costs one re-test per merge, not per push.
- Publish only ever runs after Test succeeds *in the same pipeline run*, on the *same commit* —
  so every artifact that exists is one whose exact commit was tested. There's no separate,
  always-fires publish path that could ship an untested commit.
- Deploys are separate, manual, and pull artifacts by branch (`branch: main` filter on the `ci`
  pipeline resource) — so "latest successful run" can never resolve to a PR-validation run that
  never reached Publish.

```mermaid
flowchart TD
    FeaturePush["Push to a feature branch\n(no open PR)"] --> NoTrigger["Nothing runs\ntrigger: none for plain branch pushes"]

    PROpen["PR opened / updated\ntargeting main"] --> TestPR["Stage: Test\n(build, unit, integration, E2E)\nruns against refs/pull/N/merge"]
    TestPR -->|"red"| PRBlocked["PR blocked from completing\n(required branch policy)"]
    TestPR -->|"green"| PRMergeable["PR mergeable\n(policy satisfied)"]

    PRMergeable --> Merge["PR completed → merge commit lands on main"]
    Merge --> TestMain["Stage: Test\n(same suite, now against the\nreal commit on main)"]

    TestMain -->|"red\n(rare — only if main moved\nbetween validation and merge)"| NoPublish["Stage: Publish skipped\n(dependsOn Test, condition succeeded())\nno artifact — investigate main"]
    TestMain -->|"green"| Publish["Stage: Publish\ndotnet publish admin + public\nupload pipeline artifacts"]

    Publish --> ArtifactsReady["admin + public artifacts\navailable, tagged to this main run"]

    ArtifactsReady --> ManualStaging["Manual: run 'Deploy — Staging'\n(staging powered on first)"]
    ManualStaging --> DownloadStaging["download: ci\nbranch: main filter\n→ latest successful run"]
    DownloadStaging --> DeployAdminStaging["Deploy admin site\n(migrates DB — first)"]
    DeployAdminStaging --> DeployPublicStaging["Deploy public site"]
    DeployPublicStaging --> StagingLive["staging.akerogarden.se live"]

    ArtifactsReady --> ManualProd["Manual: run 'Deploy — Production'"]
    ManualProd --> ProdApproval{"Environment approval\n(production gate)"}
    ProdApproval -->|"rejected"| ProdHalt["Deploy halted\nno changes made"]
    ProdApproval -->|"approved"| DownloadProd["download: ci\nbranch: main filter\n→ latest successful run"]
    DownloadProd --> DeployAdminProd["Deploy admin site\n(migrates DB — first)"]
    DeployAdminProd --> DeployPublicProd["Deploy public site"]
    DeployPublicProd --> ProdLive["akerogarden.se live"]

    style NoTrigger fill:#eee,stroke:#999
    style PRBlocked fill:#f8d7da,stroke:#c00
    style NoPublish fill:#f8d7da,stroke:#c00
    style ProdHalt fill:#f8d7da,stroke:#c00
    style PRMergeable fill:#d4edda,stroke:#2a2
    style ArtifactsReady fill:#d4edda,stroke:#2a2
    style StagingLive fill:#d4edda,stroke:#2a2
    style ProdLive fill:#d4edda,stroke:#2a2
```

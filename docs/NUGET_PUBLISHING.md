# Publishing NuGet Packages

This repository publishes `SteamKitSDK` and `SteamKitSDK.Windows` through NuGet Trusted Publishing. The process uses GitHub Actions OpenID Connect (OIDC), so no long-lived NuGet API key is stored in GitHub.

## One-time configuration

### 1. Create the NuGet Trusted Publishing policy

Sign in to [NuGet.org Trusted Publishing](https://www.nuget.org/account/trusted-publishing) with the NuGet account that will own the packages. Create a policy that permits publishing new packages and new versions for `SteamKitSDK` and `SteamKitSDK.Windows`.

Configure the GitHub source exactly as follows:

| Setting | Value |
| --- | --- |
| Repository owner | `pat2381` |
| Repository | `SteamKitSDK` |
| Workflow file | `publish-nuget.yml` |
| GitHub environment | `nuget-production` |

The workflow-file value is only the file name, not `.github/workflows/publish-nuget.yml`.

### 2. Configure the GitHub environment

In GitHub, open **Settings → Environments** for this repository and create `nuget-production`.

Add an environment secret named `NUGET_USER`. Its value is the NuGet.org profile name that owns the trusted-publishing policy; it is not an email address and not an API key. For additional protection, configure required reviewers for this environment.

No NuGet API key needs to be created or stored. The workflow requests a temporary key only after its Windows and cross-platform validation steps pass.

## Release process

1. Update the version in `SteamKitSDK/SteamKitSDK.csproj` and `SteamKitSDK.Windows/SteamKitSDK.Windows.csproj`.
2. Update `CHANGELOG.md`.
3. Commit and push the changes to `main`.
4. Create and publish a GitHub release for the corresponding tag, for example `v0.9.0`.
5. The **Publish NuGet packages** workflow validates, packs, and publishes both packages.
6. Confirm the new versions on NuGet.org and install them into a clean sample application.

The workflow can also be started manually from the GitHub Actions page with **Run workflow**. `--skip-duplicate` makes a retry safe when a package version was already successfully uploaded.

## Security properties

- The workflow has `id-token: write` only in the publishing job.
- NuGet verifies the repository, workflow file, and GitHub environment before issuing a temporary key.
- The temporary key is requested immediately before upload and is valid for a limited time.
- The workflow never writes a long-lived NuGet key to repository files, GitHub secrets, or build logs.

For the authoritative setup details, see the [NuGet Trusted Publishing documentation](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).

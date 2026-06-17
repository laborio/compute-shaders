# Compute Shaders

This project is set up to publish a Unity WebGL build to GitHub Pages through GitHub Actions.

## What is included

- Unity WebGL build script: `Assets/Editor/GitHubPagesWebGLBuild.cs`
- GitHub Pages workflow: `.github/workflows/deploy-webgl-pages.yml`
- Unity-focused `.gitignore`

## First-time setup

1. Create a new GitHub repository for this project.
2. Initialize git locally in this folder.
3. Commit and push to the `main` branch.
4. In GitHub, open `Settings > Pages` and set `Build and deployment` to `GitHub Actions`.
5. Add a repository secret named `UNITY_LICENSE`.

## Unity license secret

The workflow uses `game-ci/unity-builder`, which requires a Unity license in GitHub Actions.

For a personal license, the common approach is:

1. Generate a Unity activation file locally.
2. Activate it through Unity's license flow.
3. Paste the resulting license content into the `UNITY_LICENSE` GitHub secret.

GameCI's documentation covers the exact current license setup steps:

- https://game.ci/docs/github/activation
- https://game.ci/docs/github/builder

## Deploying

Every push to `main` triggers a WebGL build and deploys it to GitHub Pages.

You can also trigger it manually from the `Actions` tab with `Deploy Unity WebGL to GitHub Pages`.

## Expected site URL

Your Pages URL will be:

`https://<github-username>.github.io/<repository-name>/`

GitHub will also show the final URL on the deployed workflow run and in the repository Pages settings.

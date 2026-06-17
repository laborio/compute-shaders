# Compute Shaders

This project publishes a Unity WebGL build to GitHub Pages by committing the generated site files in `docs/`.

## GitHub Pages setup

In the GitHub repository:

1. Open `Settings > Pages`
2. Set `Build and deployment` to `Deploy from a branch`
3. Select branch `main`
4. Select folder `/docs`
5. Save

## Unity build settings

Build WebGL directly into the repository `docs` folder:

- Output folder: `docs`
- Recommended compression for simple GitHub Pages hosting: `Disabled`

After a successful build, this structure should exist:

- `docs/index.html`
- `docs/Build/...`
- `docs/TemplateData/...`
- `docs/.nojekyll`

## Publish flow

From the repository root:

```bash
git add docs
git commit -m "Publish WebGL build"
git push
```

GitHub Pages will then publish the site from `main:/docs`.

## Site URL

For this repository, the site URL is:

`https://laborio.github.io/compute-shaders/`

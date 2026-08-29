# Architecture

No application stack is locked by the harness installer. Record stack choices in
`docs/decisions/` when they constrain future work.

## Default Layering

```text
domain
  <- application
      <- infrastructure
          <- interface
              <- app surfaces
```

Inner layers must not depend on outer layers. Parse unknown input at boundaries
before it enters domain code.

## Release and update channel

`UpdateChannelService` is an infrastructure adapter for the public GitHub Releases API. The package-time `update.config.json` contains only the public repository identity and installer asset name. The adapter validates the release tag as `MAJOR.MINOR.PATCH`, uses a bounded metadata request, accepts only HTTPS GitHub download URLs, limits the installer size, and streams the package to a per-user temporary folder while reporting byte-level progress. `MainViewModel` checks on workspace entry and on a six-hour timer, defers installation while recording or processing, and exposes download/install/restart stages to the WPF update surface. The Inno Setup package is launched with silent, close-applications, and restart-applications flags; its run entry always relaunches the packaged executable after the files are copied.

The release workflow owns version advancement: it derives the next patch version from existing `v*` tags, passes that version to both .NET and Inno Setup, and publishes the resulting installer as the matching GitHub Release asset. The app never needs a GitHub credential; the CI job uses its scoped `GITHUB_TOKEN` only for publishing.

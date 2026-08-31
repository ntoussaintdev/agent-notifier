# Plan: Customizable notification sounds

Origin: Direct discussion on 2026-08-30.

## Status and purpose

Planned — not started. Allow users to customize AgentNotifier's notification
audio instead of relying solely on the built-in Windows sound mapping. The
feature should support selecting a built-in Windows sound per notification
level and, where Windows toast APIs permit it, using user-provided MP3, WAV,
or OGG files.

## Governing rules

- Preserve the current per-level defaults (`info`, `success`, `warning`, and
  `error`) when no customization is configured.
- Treat custom audio paths as local, user-controlled files; validate them
  before including them in a toast.
- Confirm the Windows App SDK toast-audio support and file-URI requirements
  before committing to a custom-file format or configuration shape.

## Progress

- [ ] Research Windows App SDK support, supported audio formats, path/URI
  requirements, and any packaging limitations for custom toast audio.
- [ ] Design persisted settings for global and per-level sound choices,
  including reset-to-default behavior and invalid/missing-file handling.
- [ ] Implement configuration, validation, and toast-audio selection while
  retaining the existing default sound mapping.
- [ ] Add tray settings UI or another discoverable local configuration flow.
- [ ] Add automated coverage and update the README with setup, supported
  formats, and fallback behavior.

## Open questions

- Does the target Windows notification API support MP3, WAV, and OGG equally
  for unpackaged applications, or is a narrower supported set required?
- Should custom sounds apply globally, per notification level, or both?
- Should missing or unsupported files silently use the level default, show an
  in-app warning, or both?

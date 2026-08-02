# Security

## Credentials

OpenAI keys, Gemini keys, Polygon API keys, and Polygon secrets are stored outside
SQLite. Each value is encrypted with Windows DPAPI using the `CurrentUser` scope and
application-specific entropy. The store uses an atomic replacement and applies a
Windows ACL for the current user. Plaintext byte buffers are cleared after use.

The application layer exposes only whether a secret exists and a fixed mask. Saving
an empty field preserves the existing value; replacement and clearing are explicit.
Exceptions are converted to actionable messages without embedding secret material.
Secrets, signatures, and decrypted values must never enter logs, test snapshots, or
source control.

## Local boundary

Kestrel listens only on `127.0.0.1`. Runtime data and secrets are not static web
assets. External writes are not background side effects: Polygon sync will begin only
from an explicit user action and AI provider adapters will have no Polygon authority.

## Generated code and attachments

Generated or attached source is untrusted. Later phases must validate attachment
type, size, filename, and archive paths before controlled storage. Attachments cannot
be executed from chat. Local C++ execution must display the required warning, avoid a
shell, restrict working directories and output, apply timeouts, kill the process tree
on cancellation, and clearly state that this is isolation rather than a security
sandbox.

## Verification

Foundation integration tests confirm a DPAPI round trip and verify that plaintext is
absent from the encrypted file. Release work must also scan tracked files and publish
artifacts for credential-shaped data before distribution.

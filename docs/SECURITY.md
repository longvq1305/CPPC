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
assets. External writes are not background side effects: Polygon sync begins only
from an explicit user action and AI provider adapters have no Polygon authority.

## Generated code and attachments

Generated or attached source is untrusted. The app validates attachment type, size,
filename, and archive paths before controlled storage. Attachments cannot be executed
from chat. Local C++ execution displays the required warning, avoids a shell,
restricts working directories and output, applies timeouts, kills the process tree on
cancellation, and clearly states that these controls are not a security sandbox.

Daily logs retain 14 days and redact recognizable bearer tokens, provider keys,
Polygon key/secret labels, and signatures. Normal logging does not include full
prompts, attachments, source files, or decrypted secrets.

## Verification

Integration tests confirm a DPAPI round trip and verify that plaintext is absent from
the encrypted file. Tests also cover path traversal, safe ZIP extraction, process
timeouts/output caps, log redaction, and request-error redaction. The publish script
verifies the pinned toolchain and runs the full suite before distribution.

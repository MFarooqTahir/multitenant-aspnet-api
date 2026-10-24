# 0005 — Email confirmation and two-factor are implemented but opt-in

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

The project must show that email confirmation and two-factor authentication are implemented,
while remaining frictionless to demo. Sending real email and enforcing a second factor by
default would make local onboarding and automated tests depend on external services.

## Decision

Implement both flows end-to-end and gate them behind configuration:

| Setting | Default | Behaviour when `false` |
| --- | --- | --- |
| `Identity:RequireConfirmedEmail` | `false` | Login is allowed for unconfirmed users; the confirmation email is still generated and sent. |
| `Identity:EnableTwoFactor` | `false` | TOTP setup/enable/disable/verify endpoints return `404 feature_disabled`; login never asks for a second factor. |

`Identity:RequireConfirmedEmail` is enforced by `AuthService`, not by Identity's own
`SignIn.RequireConfirmedEmail`/`RequireConfirmedAccount` gates, which stay off. `SignInManager`
evaluates those gates *before* the password, which made an unconfirmed address answer `403`
and an unknown one `401` — an enumeration oracle for anonymous callers. The explicit check
runs after the password succeeds, and `RefreshAsync` applies it as well so sessions minted
while confirmation was optional cannot outlive the flag being switched on. The authenticator
token provider still comes from Identity's options.

Email delivery is provider-based: `Logging` (default, writes the message to the log) and
`Smtp` (MailKit, configured under `Email:Smtp`). Swapping to SES or Azure Communication
Services means adding one `IEmailSender` implementation.

## Consequences

- Demo accounts are seeded pre-confirmed so they work regardless of the flags.
- Newly registered users start unconfirmed; when the flag is on they cannot log in until the
  confirmation link is used.
- Tests cover both sides of both flags, including a real TOTP code against Identity's
  authenticator provider.

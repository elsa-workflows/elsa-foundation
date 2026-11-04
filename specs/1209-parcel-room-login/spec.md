# Parcel Room login

**Status**: Implemented — delivery PR #3836; status takes effect when that PR merges
**Issue**: #3835 · **Goal**: none/free-flow · **Owner selection**: M2, 4 November 2026

## User scenarios and acceptance

1. A person opens the credential page and sees the selected Parcel Room design: a clear form on white, modest Elsa branding, a restrained teal action, and the full marker illustration of routing envelopes and parcels. It is the coordinated default for an unconfigured host.
2. On a narrow screen, the form appears first without horizontal scrolling. Keyboard users can reach every field, submit and optional resource link. High-contrast users retain a readable form, and unavailable artwork never prevents sign-in.
3. A host retains its explicit layout, palette, artwork, safe image/logo URL or full renderer replacement. The three Parcel Room choices remain independently selectable. Its coordinated white palette stays light under a dark OS preference.
4. Credentials, failed-sign-in feedback, antiforgery and validated return destinations retain their existing behavior. The concept's email-only, reset-password and SSO controls are not invented authentication capabilities.

## Success criteria

- At desktop, tablet, 390px and 320px, the page shows a complete uncropped illustration and usable form, with no horizontal overflow. The form precedes the artwork on narrow screens.
- The local illustration and small logo load anonymously under both shell route prefixes and proxy path bases. No third-party image or font request is required.
- A real credential sign-in succeeds through the existing form; invalid credentials and CSRF rejection retain their existing behavior. Existing custom presentation and other theme choices keep passing their contract tests.

The owner approved M2's visual direction. This work adapts its illustration into an image-only asset and real form; it does not implement new authentication flows or change Studio's workspace theme.

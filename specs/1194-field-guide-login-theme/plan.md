# Plan

1. Extend the existing Identity-owned allow-lists and bundled resources. The new illustration and logo are copied from the approved brochure assets, without regeneration.
2. Add scoped CSS for the Field Guide layout and palette in `Endpoints/LoginPage.cs`. Reuse the same form, renderer replacement, URL sanitation and antiforgery issuance. Make the drawn logo fallback layout-specific.
3. Verify actual configuration binding, both prefix forms, anonymous MIME-correct resources, real form login, custom branding, and independent choices. Check responsive, dark-OS and high-contrast rendering.
4. Document the theme recipe in `Studio/docs/branding.md`, record evidence, review the bounded diff, and follow the repository PR flow. Rebase after #3679 lands.

## Constitution check

Framework v6.1.0 and Elsa v6.3.0: changes stay in the identity implementation that already owns login presentation. No Core dependency, provider, service lifetime, domain contract, endpoint shape, permission, schema, package dependency or cross-domain reference is introduced. Existing Layer-3 options and resource delivery remain the seam. CSS and local static assets need no new service registration. Risk-based tests focus on configuration, serving and form/security contracts. No provisional architecture rule is relied on. Recheck these constraints against the finished diff.

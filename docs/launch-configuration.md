# Launch configuration — values AF Foundation must supply

Nothing organisation-specific is invented in this repository. Every value
below is a placeholder that **must** be replaced before public launch.

## Platform / organisation

| Where | Value | Current placeholder |
|-------|-------|---------------------|
| Website | Contact email / phone / address | `src/config/site.ts` → `TODO` |
| Website | Google Play URL | `src/config/site.ts` → `null` |
| Website | App Store URL | `src/config/site.ts` → `null` |
| Website | Social links | `src/config/site.ts` → `null` |
| Website | Legal review of privacy policy + terms | `routes/privacy.tsx`, `routes/terms.tsx` marked "Placeholder" |

## Infrastructure

| Where | Value |
|-------|-------|
| Coolify | `JWT_KEY` (≥ 32 random chars), `POSTGRES_PASSWORD`, public domains + TLS |
| Coolify | `ADMIN_BOOTSTRAP_KEY` — set once, **removed after the first admin exists** |
| OneSignal | App id + REST API key for API/Worker, app id baked into mobile builds |
| Email/SMS provider | `IVerificationCodeSender` implementation (dev logs codes; production needs a real sender) |
| Backups | Off-host backup schedule + verified restore |

## Medical / operational

| Where | Value |
|-------|-------|
| Deferral policy | Production `deferral_policy_versions` row approved by a qualified transfusion professional (WHO donor-selection guidance + Pakistan/Punjab requirements) with a real `approval_reference` |
| Wave configuration | Confirm radii ladder (25/50/100 km), batch size and delays as operational policy |
| Support contact | Named contact for abuse escalations |
| Moderation | At least one administrator + moderator; verify the bootstrap flow was used and the key removed |

## App store publication checklist

### Android (Google Play)
- [ ] Package id finalized (`pk.affoundation.af_blood_app`) and app signing configured
- [ ] `flutter build appbundle --dart-define=API_BASE_URL=... --dart-define=ONESIGNAL_APP_ID=...`
- [ ] Privacy policy URL (published site) in the Play console
- [ ] Data safety form (location: approximate, never shared; no health records collected)
- [ ] Location permission justification (foreground-only, consent-gated)
- [ ] Notifications permission (Android 13+ POST_NOTIFICATIONS) declared
- [ ] Deep link (`affoundation://`) — optional App Links verification if moving to https scheme
- [ ] Internal testing track first; store listing text/screenshots

### iOS (App Store)
- [ ] Bundle id, signing, capabilities (push notifications background mode)
- [ ] APNs key uploaded to OneSignal
- [ ] `flutter build ipa` with the same dart-defines
- [ ] App Privacy answers (approximate location, identifiers)
- [ ] Purpose strings: location when-in-use (consent screen), notifications
- [ ] Medical disclaimer reviewed (screen + website)
- [ ] TestFlight external review before release

## Post-launch verification

- [ ] Real push delivery confirmed on one Android + one iOS device
- [ ] Full user journey on real devices (register → consent → request → accept → report → ack)
- [ ] Backup restore drill completed with production data
- [ ] Alerting on `/health/ready` failures and outbox `failed` counts

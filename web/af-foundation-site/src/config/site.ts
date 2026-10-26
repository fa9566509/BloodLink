/**
 * Organisation-specific configuration for the AF Foundation website.
 *
 * ⚠️ EVERY value marked TODO must be supplied by AF Foundation before launch.
 * Nothing official is invented here. See docs/launch-configuration.md for the
 * full checklist.
 */
export const site = {
  foundationName: 'AF Foundation',
  tagline: 'Connecting blood donors with the people who need them',

  // TODO(af-foundation): official contact details before launch
  contact: {
    email: 'TODO-contact@af-foundation.example', // TODO(af-foundation)
    phone: 'TODO', // TODO(af-foundation)
    address: 'TODO', // TODO(af-foundation)
  },

  // TODO(af-foundation): real store URLs once the apps are published
  appStores: {
    googlePlay: null as string | null, // TODO(af-foundation): e.g. https://play.google.com/store/apps/details?id=...
    appStore: null as string | null, // TODO(af-foundation)
  },

  socials: {
    facebook: null as string | null, // TODO(af-foundation)
    instagram: null as string | null, // TODO(af-foundation)
  },

  /** Deep link scheme used by the mobile apps (must match the API notifications). */
  appDeepLinkScheme: 'affoundation://',
};

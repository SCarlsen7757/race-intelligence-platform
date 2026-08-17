/// <reference types="vite/client" />

/**
 * The hub's origin, substituted by Vite's `define` at build time.
 *
 * A global rather than an `import.meta.env` entry so it is impossible to read without declaring
 * it, and so the name appears in exactly one place in the config.
 */
declare const __HUB_URL__: string;

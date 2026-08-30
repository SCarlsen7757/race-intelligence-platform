/// <reference types="vite/client" />

/**
 * The hub's origin, substituted by Vite's `define` at build time.
 *
 * A global rather than an `import.meta.env` entry so it is impossible to read without declaring
 * it, and so the name appears in exactly one place in the config.
 */
declare const __HUB_URL__: string;

/**
 * The read API's origin, substituted by Vite's `define` at build time.
 *
 * A second address rather than a path on the hub, because history and live are two services: the
 * hub holds no database credentials and the read API holds no live state.
 */
declare const __READ_URL__: string;

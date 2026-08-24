import js from '@eslint/js';
import prettier from 'eslint-config-prettier';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import tseslint from 'typescript-eslint';

/**
 * Lint rules for the dashboard.
 *
 * Deliberately thin, and deliberately not `strictTypeChecked`. `tsconfig.json` already runs
 * `strict`, `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, `noUnusedLocals` and
 * `noUnusedParameters`, so the compiler is doing most of the work a lint config is usually asked
 * to do — and the strict preset then objects to the idioms those very flags force. It forbids the
 * `arr[i]!` that `noUncheckedIndexedAccess` requires at every array read, which would mean turning
 * a compiler guarantee into lint noise.
 *
 * What is kept is the two categories the type checker genuinely cannot see: the rules of hooks,
 * where a conditional `useEffect` compiles perfectly and misbehaves at runtime, and the type-aware
 * promise rules that catch a floating `navigate()` or an async function passed where a sync one
 * was expected.
 *
 * Formatting is Prettier's, not ESLint's — `eslint-config-prettier` turns off every stylistic rule
 * so the two never argue over the same line.
 */
export default tseslint.config(
  {
    ignores: ['dist/**', '.output/**', '.nitro/**', '.tanstack/**', 'app/routeTree.gen.ts'],
  },

  js.configs.recommended,
  tseslint.configs.recommendedTypeChecked,
  // `configs.flat.*`, not `configs.*`: the top-level ones are still eslintrc-shaped and declare
  // their plugins as an array of names, which flat config rejects outright.
  reactHooks.configs.flat['recommended-latest'],
  prettier,

  {
    languageOptions: {
      globals: { ...globals.browser, ...globals.node },
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      // Lap numbers, sector times and percentages are numbers, and interpolating one is the point
      // of every formatter in `shared/format`. Stringifying each by hand would be noise.
      '@typescript-eslint/restrict-template-expressions': ['error', { allowNumber: true }],
      // Vite substitutes __HUB_URL__ at build time; nothing declares it as a runtime global.
      'no-undef': 'off',
    },
  },

  {
    // Not in tsconfig's `include`, so the type-aware rules have no program to consult. `server.mjs`
    // is here for the same reason and is deliberately outside it: it is the production entry point,
    // and it imports `dist/server/server.js`, which does not exist until after a build. Putting it
    // in the type-checked program would make `npm run typecheck` depend on having already built.
    files: ['eslint.config.js', 'server.mjs'],
    extends: [tseslint.configs.disableTypeChecked],
  },

  {
    files: ['**/*.test.ts', '**/*.test.tsx', 'app/testing/**'],
    rules: {
      // `await act(async () => { ... })` is Testing Library's documented idiom for flushing
      // effects, and the callback usually has nothing of its own to await.
      '@typescript-eslint/require-await': 'off',
      '@typescript-eslint/unbound-method': 'off',
    },
  },
);
